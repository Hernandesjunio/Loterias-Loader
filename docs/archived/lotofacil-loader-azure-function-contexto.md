# Contexto e soluções técnicas — carregamento de resultados da Lotofácil (Azure Function)

Este documento consolida **apenas** o que foi discutido nesta conversa, para servir de base à documentação do projeto. Não inclui requisitos ou decisões que não tenham aparecido no fio de discussão.

Decisões formalizadas (fonte de verdade): `docs/adrs/0001-lotofacil-loader-azure-function.md`.

---

## 1. Objetivo do sistema

- Executar uma **Azure Function** em **C# / .NET**, hospedada no **portal Azure**.
- A função é responsável por **atualizar jogos (resultados) da Lotofácil** persistidos num **blob** no **mesmo Storage Account** onde também existirá **Table Storage** de apoio.
- O blob será **disponibilizado a aplicações externas** via **SAS token** (o consumo externo não é implementado pela função; apenas o armazenamento e atualização do conteúdo).
- O gatilho da função é **Timer Trigger**.
- Antes de buscar novos resultados, é necessário **verificar se o último resultado retornado pela API já corresponde ao último resultado já carregado** pela função (evitar trabalho redundante).
- Para suportar o algoritmo de “último carregado” vs “último disponível”, utiliza-se **Table Storage** para consultar/armazenar o **último concurso carregado**.
- Não existe “implementação padrão” pré-definida no projeto para Table Storage; o acesso será feito com **credenciais (Access Key)** guardadas em **variáveis de ambiente**.

---

## 2. Restrições e comportamento acordados

### 2.0 Calendário do sorteio (informação adicional)

- Os sorteios ocorrem **somente em dias úteis**, **às 20h**.
- Para evitar **defaults ocultos**, a “data de hoje” e o horário “20h” devem ser avaliados numa **timezone explicitamente definida** no ambiente de execução (o documento não fixa a timezone).

### 2.1 Orçamento e frequência do timer

- Inicialmente considerou-se execução **a partir das 20h**, **de hora em hora**.
- Foi ajustado pelo requisito: o **CRON pode ser a cada hora** ao longo do dia/mês, **sem receio de estourar orçamento** durante o mês.
- Na discussão foi indicado um exemplo de expressão CRON para “a cada hora, no minuto 0”: **`0 0 * * * *`** (formato com segundos, típico de Azure Functions Timer Trigger).

### 2.2 Limite da API e janela de execução da função

- O resultado virá de um **serviço de API gratuito**; em paralelo foi referido que o serviço **pago** permite **uma conexão por minuto** — logo, quando há **mais de um sorteio** a atualizar, pode ser necessário **retry** com **Polly**.
- Foi definida uma **janela máxima de 3 minutos** de trabalho na execução, para **não estourar o tempo da function** (timeout da função no host).
- Sobre a cadência de retry: foi discutido **retry a cada 30 segundos**, até um **máximo de 3 minutos** de tentativas. Também foi reconhecido que **só na primeira execução** (ou em cenários com muitas lacunas) o gargalo tende a ser mais sentido; depois tende a estabilizar.
- Na mesma discussão foi abordada a necessidade de **não violar o limite de 1 pedido por minuto** quando o fornecedor o aplicar: mecanismos mencionados incluem **respeitar 429 / `Retry-After`**, usar **Polly** para falhas transitórias, e controlar **espaçamento entre chamadas** (ex.: registo em memória de `lastApiCallUtc` e espera até completar **60 segundos** entre chamadas, **desde que ainda caiba na janela de 3 minutos**).

### 2.3 Dois modos de consulta à API

1. **Último resultado** — para saber qual é o concurso mais recente publicado e comparar com o estado persistido.
2. **Resultado por concurso (id)** — para **preencher lacunas** entre o último carregado e o último disponível.

---

## 3. Fonte de dados (API) — o que foi descrito na conversa

### 3.1 Endpoints (padrão de URL)

Na conversa foram indicados estes padrões (com **token** passado por query string):

- Último concurso:  
  `https://www.lotodicas.com.br/api/v2/lotofacil/results/last?token=<TOKEN>`
- Concurso específico:  
  `https://www.lotodicas.com.br/api/v2/lotofacil/results/{id}?token=<TOKEN>`

O valor concreto do `<TOKEN>` foi mencionado na conversa para exemplificação; **na arquitetura discutida**, o token **não deve ficar hardcoded** no código-fonte — deve vir de **configuração / variável de ambiente** (foi explícito na discussão: não persistir segredos em código).

### 3.2 Formato de resposta JSON (exemplo fornecido)

Foi fornecido um exemplo de corpo de resposta (estrutura discutida):

```json
{
  "code": 200,
  "data": {
    "cities": [],
    "draw_date": "2026-04-24",
    "draw_number": 3669,
    "drawing": {
      "draw": [2, 3, 4, 5, 8, 9, 10, 11, 12, 15, 16, 17, 22, 23, 24],
      "month": null,
      "team": null
    },
    "has_winner": false,
    "next_draw_date": "2026-04-25",
    "next_draw_prize": 10000000.0,
    "prizes": [
      { "name": "15 acertos", "prize": 0.0, "winners": 0 },
      { "name": "14 acertos", "prize": 1760.21, "winners": 227 }
    ]
  },
  "status": "success"
}
```

*(O exemplo na conversa continha mais entradas em `prizes`; a estrutura relevante discutida para mapeamento é `draw_number`, `draw_date`, `drawing.draw`, e a entrada de prémios associada a “15 acertos”.)*

### 3.3 Mapeamento discutido (API → modelo do blob)

Foi explicitado na conversa como derivar os campos do blob a partir deste JSON:

| Campo no blob (nome acordado) | Origem no JSON da API |
|-------------------------------|------------------------|
| `contest_id` | `data.draw_number` |
| `draw_date` | `data.draw_date` |
| `numbers` | `data.drawing.draw` (lista de números) |
| `winners_15` | em `data.prizes`, o item cujo `name` é **`"15 acertos"`**, campo **`winners`** |
| `has_winner_15` | **`true`** se `winners_15 > 0`, caso contrário **`false`** |

---

## 4. Formato do documento no Blob Storage

Foi definido que o blob conterá um JSON com uma coleção `draws`, onde cada item segue este formato (exemplo dado na conversa):

```json
{
  "draws": [
    {
      "contest_id": 1,
      "draw_date": "2003-09-29",
      "numbers": [2, 3, 5, 6, 9, 10, 11, 13, 14, 16, 18, 20, 23, 24, 25],
      "winners_15": 5,
      "has_winner_15": true
    }
  ]
}
```

Outros detalhes discutidos sobre o blob:

- **Nome do blob**: **`Lotofacil`** (conforme pedido).
- O blob reside no **mesmo Storage Account** que a função utiliza para estado (Table).
- Para consumo externo via SAS, foi mencionado definir **`Content-Type`** adequado para JSON (ex.: `application/json; charset=utf-8`) e gravar o blob de forma que leitores externos não vejam conteúdo parcial inconsistente (na discussão: escrita/atualização coerente do documento completo, não “append” quebrado).

---

## 5. Table Storage — papel e conteúdo lógico

### 5.1 Função na solução

- Armazenar e consultar o **último concurso já carregado** pela função (`last loaded`), para:
  1. **Comparar** com o “último” devolvido pela API antes de iniciar downloads em massa.
  2. **Calcular lacunas** (intervalo de `id`s) entre o último persistido e o último disponível.

### 5.2 Modelo de entidade discutido (nomes propostos na conversa)

Na discussão foram propostos (como exemplo de modelação):

- **Nome da tabela**: `LotofacilState`
- **PartitionKey** fixo: `Lotofacil`
- **RowKey** fixo: `Loader` (uma linha lógica de estado)
- Campos lógicos:
  - `LastLoadedContestId` (inteiro)
  - `LastLoadedDrawDate` (data; derivada de `data.draw_date` do último concurso carregado)
  - `LastUpdatedAtUtc` (data/hora)
  - Uso de **ETag** do Azure Table para **concorrência otimista** (evitar duas instâncias a sobrescrever estado de forma inconsistente).

### 5.3 Credenciais

- Acesso ao Table Storage via **Access Key** (ou cadeia de ligação equivalente) em **variáveis de ambiente**, conforme requisito explícito.

---

## 6. Algoritmo de atualização (passo a passo, como descrito)

1. **Ler** do Table Storage o registo do último concurso carregado (`lastLoaded`).
2. **Encerramento antecipado para evitar chamadas desnecessárias** (antes de chamar a API):
   - Se **hoje não é dia útil**: **terminar** (não há sorteio).
   - Se **hoje é dia útil** e **ainda não passou das 20h** (na timezone definida): **terminar** (ainda não existe sorteio “de hoje”).
   - Se **hoje é dia útil**, **já passou das 20h**, e `LastLoadedDrawDate == hoje`: **terminar** (o sorteio do dia já foi carregado).
2. Chamar o endpoint **`/results/last`** (uma chamada).
3. Obter `latestId` a partir de `data.draw_number`.
4. Se **`latestId <= lastLoaded`**: **terminar** — não há novos concursos a carregar (o “último resultado retornado” já está alinhado com o que a função já tinha persistido).
5. Caso contrário, calcular a lista de **concursos em falta**: de `lastLoaded + 1` até `latestId` (lacunas).
6. Executar o processamento dentro de uma **janela interna de 3 minutos** (cancelamento quando a janela expira).
7. Para cada `id` em falta (em ordem crescente, sem paralelismo agressivo discutido):
   - Chamar **`/results/{id}`** com política de **retry/resiliência** (Polly) e **controlo de cadência** compatível com **1 pedido/minuto** quando aplicável.
   - Se a janela de 3 minutos terminar antes de concluir todos os ids, **parar**; na **próxima execução horária** o processo **retoma** a partir do estado persistido.
8. **Persistência discutida (ordem)**:
   - **Primeiro** atualizar/gravar o **blob** com o documento JSON completo (`draws` atualizado).
   - **Depois** atualizar o **Table Storage** com o novo `LastLoadedContestId`, `LastLoadedDrawDate` (do último concurso processado) e timestamp, para não marcar como carregado um concurso cujo blob falhou ao gravar.

### 6.1 Idempotência e concorrência (mencionado na conversa)

- A função deve tolerar **reexecuções** (timer) sem corromper dados: mesma entrada lógica ⇒ comportamento seguro ao repetir.
- Foi mencionado o risco de **duas instâncias** e o uso de **ETag** no Table como mitigação.

---

## 7. Estrutura de projeto e princípios (como abordados na conversa)

### 7.1 Organização de pastas / camadas (proposta na discussão)

Foi sugerida uma estrutura em **um único projeto** de Azure Functions (adequação ao **Consumption plan**: menos atrito de deploy, contenção de cold start), com separação clara:

- `FunctionApp/` — ficheiro(s) do **TimerTrigger**; **só orquestração**, sem concentrar toda a lógica de negócio.
- `Application/` — casos de uso (ex.: `UpdateLotofacilResults`), **portas** (`ILotofacilApiClient`, `ILotofacilStateStore`, `ILotofacilBlobStore`), fábricas/policies Polly.
- `Domain/` — modelos (`LotofacilDraw`, documento do blob, DTOs da API).
- `Infrastructure/` — implementações: cliente HTTP da API, `TableClient` / `BlobClient` (Azure SDK).
- `Composition/` — extensões de `IServiceCollection` para **DI**, registo de **HttpClientFactory**, clientes Azure e pipelines Polly.

### 7.2 Princípios e boas práticas citados

- **SOLID** e **Clean Code**, evitar **antipatterns**.
- **Inversão de dependência**: regras no núcleo da aplicação dependem de **interfaces (portas)**; infraestrutura implementa **adaptadores**.
- **HttpClientFactory** (em vez de padrões que causem exaustão de sockets).
- **Sem paralelismo agressivo** entre chamadas à API quando o objetivo é respeitar **1 pedido/minuto**.
- **Logging estruturado** com informação útil (intervalo de concursos pendente, quantos foram atualizados, motivo de paragem: janela de tempo, rate limit, etc.) — mencionado como boa prática na discussão.

### 7.3 Nomes discutidos

- Nome alternativo ao sugerido `cargalotofacil`: **`Lotofacil.Results.Loader`** (projeto) e nome de função exemplo **`LotofacilResultsUpdater`**.
- Tabela de estado exemplo: **`LotofacilState`**.
- Blob: nome **`Lotofacil`**.

*(O nome exato do **contentor** do blob não foi fixado pelo utilizador na conversa; apenas o nome do blob. Qualquer nome de contentor permanece decisão de configuração/deploy.)*

---

## 8. Configuração / variáveis de ambiente (nomes sugeridos na conversa)

Na discussão foram propostos estes nomes (padrão `Section__Key` comum em .NET / Azure):

| Variável (exemplo na conversa) | Finalidade |
|--------------------------------|------------|
| `Lotodicas__BaseUrl` | Base da API (`https://www.lotodicas.com.br`) |
| `Lotodicas__Token` | Token de query string (segredo) |
| `Storage__ConnectionString` | Ligação ao Storage Account (mencionado como opção simples na discussão) |
| `Storage__BlobContainer` | Nome do contentor onde está o blob `Lotofacil` |
| `Storage__LotofacilBlobName` | Nome do blob (`Lotofacil`) |
| `Storage__LotofacilStateTable` | Nome da tabela de estado (`LotofacilState`) |

Foi também mencionada a alternativa de configurar conta por **nome + chave** em vez de connection string, desde que equivalente ao requisito de **Access Key em ambiente**.

---

## 9. Polly e política de resiliência (pontos explícitos da conversa)

- Utilizar **Polly** para **retries** em falhas **transitórias** (ex.: timeouts, erros 5xx, **429**).
- **Timeout por pedido** HTTP (valores numéricos concretos foram mencionados apenas como exemplo na conversa, ex.: 10–15 s — tratar como **parâmetro configurável** se for para documentação operacional).
- **Backoff / intervalo entre tentativas**: discutido **30 segundos** entre tentativas, até **no máximo 3 minutos** de tentativas dentro da execução.
- **Respeitar `Retry-After`** quando a API devolver **429** (quando existir).
- **Pacing** adicional: garantir que **não se exceda 1 chamada por minuto** ao fornecedor quando essa regra estiver em vigor — por exemplo, registar `lastApiCallUtc` e **esperar o restante de 60 s** antes da próxima chamada, **desde que ainda exista tempo na janela de 3 minutos**.

---

## 10. Documentação auxiliar no Cursor (Rules / Skills / Agents)

Foi feita uma avaliação **criteriosa** na conversa:

- **Rule (recomendado)**: **uma** regra curta no Cursor pode ajudar a **evitar regressões** (ex.: proibir token em código, obrigar HttpClientFactory, manter trigger fino). **Justificativa dada**: o projeto tende a crescer e regras curtas reduzem inconsistência.
- **Skill / Agents**: **não necessários** neste momento. **Justificativa dada**: o escopo é **bem definido** e não é um padrão repetitivo de scaffolding em massa.

---

## 11. Itens explicitamente fora do escopo desta conversa

O fio de discussão **não** detalhou, entre outros:

- Implementação concreta de **SAS** (geração, rotação, permissões).
- **CI/CD**, nomes de **Resource Group**, **SKU** além de referência ao **Consumption plan**.
- Testes automatizados, embora o repositório tenha orientação geral de testes em outros documentos — **não foram acordados nesta conversa** para esta função.

---

## 12. Estado de implementação

Até ao momento desta conversa, o que existiu foi **planeamento e decisões de desenho**. A **implementação de código** foi condicionada a aprovação explícita do utilizador (“só executar quando eu aprovar”); a criação deste markdown **não** implica que o código já tenha sido gerado no repositório.

---

## Referências

- `docs/adrs/0001-lotofacil-loader-azure-function.md`

*Documento gerado a partir do histórico desta conversa, sem adicionar requisitos não discutidos.*
