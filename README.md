# Lotofacil-Loader

Atualizador de resultados da **Lotofácil** executado como **Azure Function** (**C# / .NET**) com **Timer Trigger**.

O sistema mantém um **JSON** num **Blob Storage** (para consumo externo via **SAS token**) e usa **Azure Table Storage** como estado para saber **qual foi o último concurso carregado**, evitando trabalho redundante e permitindo retomar atualizações em execuções futuras.

## O que este projeto faz

- **Executa por timer** (Azure Functions Timer Trigger).
- **Consulta uma API externa** para:
  - descobrir o **último concurso** publicado;
  - buscar o **resultado por concurso (id)** para preencher lacunas.
- **Atualiza um blob JSON** (nome do blob: `Lotofacil`) com uma coleção de `draws`.
- **Persiste estado no Table Storage** (último concurso carregado), para:
  - comparar “último carregado” vs “último disponível” antes de processar;
  - retomar do ponto certo se faltar tempo numa execução.

## Fonte de verdade (documentação)

Este repositório segue uma abordagem **docs-first**. As fontes de verdade são:

- `docs/adrs/0001-lotofacil-loader-azure-function.md`
- `docs/spec-driven-execution-guide.md` (inclui o **Contrato V0** normativo)
- `docs/fases-execucao-templates.md`

## Dados persistidos no blob

O blob contém um documento JSON com a chave `draws`. Cada item inclui:

- `contest_id`
- `draw_date`
- `numbers`
- `winners_15`
- `has_winner_15`

O mapeamento de campos (API → blob) e o formato completo estão detalhados em `docs/spec-driven-execution-guide.md` (Contrato V0).

## Carga inicial do blob (bulk / layout CEF)

Se você iniciar “do zero” e depender apenas da API (com pacing de 1 req/min), a carga completa pode levar dias.
Para evitar isso, use uma fonte **bulk** (layout histórico da CEF em CSV) e converta para o **JSON canônico** do blob.

### Gerar o JSON canônico a partir do CSV da CEF

Este repositório inclui um script Python (sem dependências externas) que converte o CSV da CEF para o formato:

```json
{ "draws": [ { "contest_id": 1, "draw_date": "YYYY-MM-DD", "numbers": [..15..], "winners_15": 0, "has_winner_15": false } ] }
```

- **Script**: `tools/cef_to_blob.py`
- **Entrada**: CSV da CEF (geralmente `;` e data `dd/MM/yyyy`)
- **Saída**: JSON `UTF-8` com `draws` ordenado por `contest_id`

Exemplo (Git Bash / Windows):

```bash
python tools/cef_to_blob.py --input "C:\caminho\para\lotofacil.csv" --output "C:\caminho\para\Lotofacil.json" --pretty
```

O script também suporta dados copiados do Excel (normalmente separados por **TAB**). Basta salvar o conteúdo em um arquivo `.tsv` (ou `.txt`) e rodar do mesmo jeito.
Ele também funciona **sem header**: se a primeira linha não parecer um header, o script assume o layout posicional:

- `Concurso`, `Data Sorteio`, `Bola1..Bola15`, (`Ganhadores 15 acertos` opcional)

### Subir o JSON no Blob Storage

Depois de gerar o arquivo, carregue-o no seu container configurado em:

- `Storage__BlobContainer`
- `Storage__LotofacilBlobName` (no contrato V0, o nome canônico do blob é `Lotofacil`)

Você pode subir com Azure Portal, `az storage blob upload`, Storage Explorer, etc.
Após essa carga inicial, a Function passa a atuar como **incremental**, preenchendo apenas concursos novos.

## Estado no Table Storage (alto nível)

O Table Storage armazena o **último concurso carregado** para o processo de atualização.
Na conversa foram propostos (como exemplo) nomes como `LotofacilState` e um registo único de estado, incluindo `LastLoadedContestId` e `LastUpdatedAtUtc`, com uso de **ETag** para concorrência otimista.

## Restrições e comportamento (resumo)

- **Frequência do timer**: **configurável por ambiente** via `LotofacilLoader__TimerSchedule` (ex.: `0 0 * * * *`).
- **Janela de execução**: processamento com **janela interna máxima de 3 minutos**.
- **Rate limit / resiliência**: quando houver limitação (ex.: **1 pedido/minuto**) e/ou respostas **429**, o fluxo considera **retry** (Polly) e respeito a `Retry-After` quando existir, desde que caiba na janela.
- **Ordem de persistência**: primeiro **gravar o blob**, depois **atualizar o estado** no Table Storage.

## Configuração (variáveis de ambiente)

Os nomes abaixo foram sugeridos na conversa como padrão de configuração:

- `LotofacilLoader__TimerSchedule`
- `Lotodicas__BaseUrl`
- `Lotodicas__Token`
- `Storage__ConnectionString`
- `Storage__BlobContainer`
- `Storage__LotofacilBlobName`
- `Storage__LotofacilStateTable`

Os segredos (ex.: token e credenciais do Storage) **não devem** ficar hardcoded no código-fonte.

## Execução local (exemplo de `local.settings.json`)

Para rodar a Azure Function localmente (Azure Functions Core Tools), as variáveis podem ser fornecidas via `src/FunctionApp/local.settings.json` (**não versionar**).

O exemplo abaixo contém:

- chaves **operacionais do host** (necessárias para o runtime local iniciar);
- chaves **do domínio (V0)** (normativas no contrato em `docs/spec-driven-execution-guide.md`, seção “Entradas canônicas”).

> Observação: os valores abaixo são placeholders. Não comite tokens/segredos.

```json
{
  "IsEncrypted": false,
  "Values": {
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "AzureWebJobsStorage": "<connection-string-para-storage-ou-emulador>",

    "LotofacilLoader__TimerSchedule": "0 0 * * * *",
    "Lotodicas__BaseUrl": "https://www.lotodicas.com.br",
    "Lotodicas__Token": "<seu-token>",

    "Storage__ConnectionString": "<connection-string-do-storage>",
    "Storage__BlobContainer": "<nome-do-container>",
    "Storage__LotofacilBlobName": "Lotofacil",
    "Storage__LotofacilStateTable": "LotofacilState"
  }
}
```

## Hooks de Git (qualidade local)

Este repositório inclui um hook `pre-push` para **bloquear pushes** quando `dotnet test` falhar.

- **Instalar (Git Bash)**:

```bash
./scripts/install-git-hooks.sh
```

## Não-objetivos

- Não há promessa de “previsão”, “melhor chance” ou qualquer garantia de resultado.
- Não há implementação de consumo externo do blob (o consumo via SAS é responsabilidade de quem consome).

