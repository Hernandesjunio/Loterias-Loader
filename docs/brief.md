# Brief — Lotofacil-Loader (Azure Function)

## Objetivo

Construir uma **Azure Function** (C# / .NET), hospedada no **Azure**, com **Timer Trigger**, responsável por **atualizar resultados da Lotofácil** persistidos num **blob** no mesmo **Storage Account** onde também existe **Table Storage** de apoio.

O blob será disponibilizado a aplicações externas via **SAS token** (o consumo externo não é implementado aqui; apenas o armazenamento e atualização do conteúdo).

## Escopo (o que está dentro)

- **Timer Trigger** para orquestração do carregamento.
- Consulta a uma API externa em dois modos:
  - **último resultado** (`/results/last`) para descobrir o concurso mais recente publicado;
  - **resultado por concurso** (`/results/{id}`) para preencher lacunas.
- Persistência de:
  - um **documento JSON** no Blob Storage (nome do blob: `Lotofacil`);
  - o estado de “último concurso carregado” no **Table Storage** (para comparar e retomar).
- Resiliência com **Polly** para falhas transitórias e cenários de **rate limit** (incluindo 429 e `Retry-After` quando presente), respeitando a janela máxima por execução.

## Não-objetivos

- Prometer ou sugerir “previsão”, “melhor chance” ou garantia de resultado.
- Implementar o consumo externo do blob via SAS (somente armazenar/atualizar o conteúdo).
- Fixar detalhes que não foram fechados na conversa (por exemplo, o **nome do contentor** do blob não foi definido).
- Definir CI/CD, Resource Group, SKUs além da referência ao **Consumption plan**, ou rotação/permissões de SAS (não discutidos).

## Restrições e comportamento acordados

- **Calendário do sorteio**: os sorteios ocorrem **somente em dias úteis**, **às 20h**. A avaliação de “hoje” e “20h” depende de uma **timezone explicitamente definida** no ambiente de execução (este documento não fixa a timezone).
- **Frequência**: o schedule do Timer Trigger deve ser **configurável por ambiente** (sem hardcode em código). Valor recomendado para execução “a cada hora”: `0 0 * * * *` (formato com segundos típico do Timer Trigger do Azure Functions).
- **Janela máxima por execução**: **3 minutos** de trabalho (janela interna), para não estourar o tempo da function no host.
- **API e cadência**:
  - o resultado vem de um serviço gratuito; foi referido que o serviço pago permite **uma conexão por minuto**;
  - em cenários com mais de um sorteio para atualizar, pode ser necessário **retry** com **Polly**;
  - foi discutido retry a cada **30 segundos**, até um máximo de **3 minutos** de tentativas dentro de uma execução;
  - quando aplicável, respeitar **429** e `Retry-After`, e espaçar chamadas (ex.: esperar completar **60 segundos** entre chamadas) desde que ainda caiba na janela.
- **Sem segredos em código**: token e credenciais (Access Key / equivalente) devem vir de **variáveis de ambiente**.

## Contrato de dados (alto nível)

### Blob (documento JSON)

O blob contém um JSON com a coleção `draws`. Cada item inclui:

- `contest_id`
- `draw_date`
- `numbers`
- `winners_15`
- `has_winner_15` (derivado de `winners_15 > 0`)

Mapeamento discutido (API → blob):

- `contest_id` ← `data.draw_number`
- `draw_date` ← `data.draw_date`
- `numbers` ← `data.drawing.draw`
- `winners_15` ← em `data.prizes`, o item cujo `name` é `"15 acertos"`, campo `winners`
- `has_winner_15` ← `true` se `winners_15 > 0`, senão `false`

### Table Storage (estado)

Papel: armazenar/consultar o **último concurso carregado** (“last loaded”) para:

1. comparar com o “último” devolvido pela API antes de iniciar downloads em massa;
2. calcular lacunas (intervalo de ids) entre o último persistido e o último disponível.

Na conversa foram propostos (como exemplo):

- tabela: `LotofacilState`
- `PartitionKey`: `Lotofacil`
- `RowKey`: `Loader`
- campos lógicos: `LastLoadedContestId`, `LastUpdatedAtUtc`
- campos lógicos adicionais: `LastLoadedDrawDate` (data do último concurso carregado; derivada de `data.draw_date`)
- uso de **ETag** para concorrência otimista

## Algoritmo de atualização (resumo)

1. Ler do Table Storage o `lastLoaded`.
2. Encerramento antecipado (antes de chamar a API), para evitar chamadas desnecessárias:
   - se hoje não é dia útil, encerrar;
   - se hoje é dia útil e ainda não passou das 20h (na timezone definida), encerrar;
   - se hoje é dia útil, já passou das 20h e `LastLoadedDrawDate == hoje`, encerrar.
3. Chamar o endpoint `/results/last` e obter `latestId` via `data.draw_number`.
3. Se `latestId <= lastLoaded`, encerrar (não há novos concursos a carregar).
4. Caso contrário, calcular ids em falta de `lastLoaded + 1` até `latestId` e processar dentro de uma janela interna de **3 minutos**.
5. Persistência na ordem discutida:
   - primeiro **gravar o blob** com o documento JSON completo atualizado;
   - depois **atualizar o Table Storage** com o novo `LastLoadedContestId`, `LastLoadedDrawDate` e timestamp.

## Configuração (variáveis de ambiente)

Nomes sugeridos na conversa (padrão `Section__Key`):

- `LotofacilLoader__TimerSchedule`
- `Lotodicas__BaseUrl`
- `Lotodicas__Token`
- `Storage__ConnectionString`
- `Storage__BlobContainer`
- `Storage__LotofacilBlobName`
- `Storage__LotofacilStateTable`

## Referências

- `docs/adrs/0001-lotofacil-loader-azure-function.md`
- `docs/spec-driven-execution-guide.md`
- `docs/fases-execucao-templates.md`
- `docs/archived/lotofacil-loader-azure-function-contexto.md` (histórico inicial; não normativo)
- [`../AGENTS.md`](../AGENTS.md)

