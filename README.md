# Loterias-Loader

Atualizador de resultados de **Lotofácil**, **Mega-Sena** e **Quina**, executado como **Azure Function** (**C# / .NET**) com **Timer Trigger**.

O sistema mantém **blobs JSON** (consumo externo via **SAS token**) e usa **Azure Table Storage** (`LoteriasState`) para estado por modalidade e rotação do scheduler.

## O que este projeto faz

- **Executa por timer** a cada **10 minutos** (`LoteriasLoader__TimerSchedule`, ex.: `0 */10 * * * *`).
- **Processa uma loteria por tick** (padrão): rotação `lotofacil` → `mega_sena` → `quina`, com índice persistido em `_scheduler/modality_rotation`.
- **Consulta a API Lotodicas** via **`GET /api/v2/{modalidade}/results/all`** — **1 request HTTP** por execução/modalidade (sync bulk; ver ADR 0003).
- **Atualiza o blob** da modalidade selecionada (`Lotofacil`, `MegaSena`, `Quina`) com a coleção `draws`.
- **Persiste estado no Table Storage** (`LastLoadedContestId`, etc.) para retomar progresso entre ticks.

Com timer de 10 minutos e 3 modalidades, **cada loteria é sincronizada aproximadamente a cada 30 minutos**.

## Fonte de verdade (documentação)

Este repositório segue uma abordagem **docs-first**. As fontes de verdade são:

- `docs/brief.md`
- `docs/spec-driven-execution-guide.md` (inclui o **Contrato V0** normativo)
- `docs/adrs/0003-sync-bulk-results-all-only.md`
- `docs/fases-execucao-templates.md`
- `AGENTS.md` (mapa para agentes de IA)

## Dados persistidos no blob

Cada modalidade tem seu blob. Todos contêm `draws` com campos canônicos (variam por loteria):

| Modalidade | Blob (exemplo) | Campos de ganhadores |
|------------|----------------|----------------------|
| Lotofácil | `Lotofacil` | `winners_15`, `has_winner_15` |
| Mega-Sena | `MegaSena` | `winners_6`, `has_winner_6` |
| Quina | `Quina` | `winners_5`, `has_winner_5` |

Campos comuns: `contest_id`, `draw_date`, `numbers`.

O mapeamento API → blob está detalhado em `docs/spec-driven-execution-guide.md` (Contrato V0).

## Carga inicial do blob (bulk / layout CEF)

Se você iniciar “do zero” e depender apenas da API, a carga completa pode levar muito tempo.
Para bootstrap, use fonte **bulk** (CSV histórico da CEF) e converta para o **JSON canônico** do blob.

### Gerar o JSON canônico a partir do CSV da CEF

Scripts Python (sem dependências externas) em `tools/`:

#### Lotofácil

```json
{ "draws": [ { "contest_id": 1, "draw_date": "YYYY-MM-DD", "numbers": [..15..], "winners_15": 0, "has_winner_15": false } ] }
```

```bash
python tools/lotofacil_cef_to_blob.py --input "C:\caminho\para\lotofacil.csv" --output "C:\caminho\para\Lotofacil.json" --pretty
```

#### Mega-Sena

```json
{ "draws": [ { "contest_id": 1, "draw_date": "YYYY-MM-DD", "numbers": [..6..], "winners_6": 0, "has_winner_6": false } ] }
```

```bash
python tools/mega_sena_cef_to_blob.py --input "C:\caminho\para\megasena.csv" --output "C:\caminho\para\MegaSena.json" --pretty
```

#### Quina

```json
{ "draws": [ { "contest_id": 1, "draw_date": "YYYY-MM-DD", "numbers": [..5..], "winners_5": 0, "has_winner_5": false } ] }
```

```bash
python tools/quina_cef_to_blob.py --input "C:\caminho\para\quina.csv" --output "C:\caminho\para\Quina.json" --pretty
```

Os scripts aceitam CSV (`;`), TSV (Excel) ou layout posicional sem header — ver comentários em cada script.

### Subir o JSON no Blob Storage

Carregue cada arquivo no container configurado em `Storage__BlobContainer`, usando os nomes em `Storage__*BlobName`.
Após o bootstrap, a Function mantém os blobs atualizados via sync bulk da API.

## Estado no Table Storage (alto nível)

Tabela **`LoteriasState`**:

- **Por modalidade**: último concurso carregado (`LastLoadedContestId`, `LastUpdatedAtUtc`, ETag).
- **Scheduler**: `PartitionKey=_scheduler`, `RowKey=modality_rotation` — controla qual loteria roda no próximo tick.

## Restrições e comportamento (resumo)

- **Frequência do timer (produção)**: **`0 */10 * * * *`** — a cada **10 minutos** via `LoteriasLoader__TimerSchedule`.
- **Rotação de modalidade (padrão)**: **1 modalidade por tick**; ordem default `lotofacil,mega_sena,quina` (`LoteriasLoader__ModalityOrder`).
- **Modo legado**: `LoteriasLoader__SequentialAllModalities=true` executa as 3 modalidades na mesma invocação.
- **Sync API**: exclusivamente **`/results/all`** por modalidade (sem `/last` nem `/by_id`).
- **Janela de execução**: **180 segundos** por modalidade (`IExecutionBudget`).
- **Rate limit / resiliência**: retry capado pelo budget; respeita `Retry-After` em 429 quando couber na janela.
- **Ordem de persistência**: primeiro **blob**, depois **estado** no Table Storage.
- **Cancelamento pelo host** (timeout ~5 min Azure): scheduler **não avança** — retoma a mesma modalidade no próximo tick.

## Configuração (variáveis de ambiente)

Principais chaves:

- `LoteriasLoader__TimerSchedule` — ex.: `0 */10 * * * *`
- `LoteriasLoader__SequentialAllModalities` (opcional; default `false`)
- `LoteriasLoader__ModalityOrder` (opcional; default `lotofacil,mega_sena,quina`)
- `LotofacilLoader__TimerSchedule` (compatibilidade)
- `Lotodicas__BaseUrl`, `Lotodicas__Token`
- `Storage__ConnectionString`, `Storage__BlobContainer`
- `Storage__LotofacilBlobName`, `Storage__MegasenaBlobName`, `Storage__QuinaBlobName`
- `Storage__LoteriasStateTable` (valor normativo: `LoteriasState`)

Segredos (token, connection strings) **não devem** ficar hardcoded no código-fonte.

## Execução local (exemplo de `local.settings.json`)

Variáveis via `src/FunctionApp/local.settings.json` (**não versionar**).

> Valores abaixo são placeholders. Não comite tokens/segredos.

```json
{
  "IsEncrypted": false,
  "Values": {
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "AzureWebJobsStorage": "<connection-string-para-storage-ou-emulador>",

    "LoteriasLoader__TimerSchedule": "0 */10 * * * *",
    "LoteriasLoader__SequentialAllModalities": "false",
    "Lotodicas__BaseUrl": "https://www.lotodicas.com.br",
    "Lotodicas__Token": "<seu-token>",

    "Storage__ConnectionString": "<connection-string-do-storage>",
    "Storage__BlobContainer": "<nome-do-container>",
    "Storage__LotofacilBlobName": "Lotofacil",
    "Storage__MegasenaBlobName": "MegaSena",
    "Storage__QuinaBlobName": "Quina",
    "Storage__LoteriasStateTable": "LoteriasState",

    "Logging__LogLevel__Default": "Information",
    "Logging__LogLevel__Lotofacil.Loader": "Debug"
  }
}
```

Para **testes locais**, você pode acelerar o timer (ex.: a cada minuto: `0 * * * * *`).

## Deploy manual (gerar ZIP em Release)

### Gerar pacote (Release)

Na raiz do repositório:

```bash
dotnet publish "src/FunctionApp/Lotofacil.Loader.FunctionApp.csproj" -c Release -o "artifacts/publish"
```

Gerar o ZIP a partir do output publicado:

```bash
cd artifacts/publish && zip -r "../functionapp.zip" .
```

O arquivo final fica em `artifacts/functionapp.zip`.

### Publicar o ZIP (opções)

- **Azure Portal (Zip Deploy)**: Function App → *Deployment Center* → Zip Deploy → enviar `artifacts/functionapp.zip`.
- **Azure Functions Core Tools**:

```bash
func azure functionapp publish "<NOME_DA_FUNCTION_APP>"
```

## Hooks de Git (qualidade local)

Hook `pre-push` bloqueia push quando `dotnet test` falhar.

```bash
./scripts/install-git-hooks.sh
```

## Não-objetivos

- Não há promessa de “previsão”, “melhor chance” ou qualquer garantia de resultado.
- Não há implementação de consumo externo do blob (consumo via SAS é responsabilidade de quem consome).
