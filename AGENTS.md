# Orientação para agentes de IA

Índice operacional: `README.md`. Contrato normativo: `docs/spec-driven-execution-guide.md`.

Este arquivo é um **mapa cognitivo mínimo** para agentes trabalhando neste repositório. Ele não substitui os specs. Se uma mudança afetar **semântica**, **contrato** ou **métricas/indicadores**, atualize **docs + testes + código** juntos.

## Intenção do repositório

Azure Function (**C# / .NET**) com **Timer Trigger** que mantém blobs JSON de resultados de **três loterias** (Lotofácil, Mega-Sena, Quina) sincronizados com a API Lotodicas, com estado mínimo em **Table Storage** para retomada e rotação entre modalidades.

Comportamento determinístico e rastreável:

- cada tick processa **uma modalidade** (rotação `lotofacil` → `mega_sena` → `quina`);
- sync via **`GET /api/v2/{modalidade}/results/all`** (1 request HTTP por execução — ADR 0003);
- persistência em ordem: **blob primeiro**, **estado depois**;
- logs/traces com `run_id`, `modality`, `reason_stop`.

## Modelo operacional (produção)

| Aspecto | Valor |
|---------|--------|
| Timer | **`0 */10 * * * *`** — a cada **10 minutos** (NCRONTAB Azure, 6 campos) |
| Modalidades por tick | **1** (padrão; rotação via `_scheduler/modality_rotation` em `LoteriasState`) |
| Cadência por loteria | **~30 minutos** (3 modalidades × 10 min) |
| Janela interna | **180s** por modalidade (`IExecutionBudget`) |
| Modo legado | `LoteriasLoader__SequentialAllModalities=true` — 3 modalidades na mesma invocação |

Configuração do schedule: `LoteriasLoader__TimerSchedule` (compatível com `LotofacilLoader__TimerSchedule`).

## Anti-objetivos

- promessas preditivas/garantias (“vai acontecer”, “vai melhorar chance”, etc.);
- defaults ocultos no servidor quando o pedido é ambíguo;
- reintroduzir fluxo incremental (`/last`, `/by_id`) sem ADR explícita.

## Fontes de verdade (ordem sugerida)

1. `docs/brief.md` — escopo, restrições, não-objetivos
2. `docs/spec-driven-execution-guide.md` — **Contrato V0** normativo (timer, rotação V0.2, janela 180s)
3. `docs/adrs/0003-sync-bulk-results-all-only.md` — sync exclusivo via `/results/all`
4. `docs/adrs/0001-lotofacil-loader-azure-function.md` — arquitetura em camadas
5. `docs/adrs/0002-observabilidade-logs-debug-e-tracing.md` — logs e tracing
6. `docs/contract-test-plan.md` — fixtures/goldens e ordem dos testes de contrato
7. `docs/test-plan.md` — matriz de cobertura (incl. rotação G, budget H, bulk J, lifetime K)
8. `docs/spec-driven-execution-guide.md` → ordem prática em `docs/fases-execucao-templates.md`
9. `docs/project-guide.md` — estrutura e fronteiras de camadas
10. `docs/glossary.md` — linguagem humana (opcional)

## Guia operacional (execução local)

- Exemplo de `local.settings.json`: seção **“Execução local”** em `README.md`.
- Testes de contrato: `dotnet test tests/ContractTests.V0/ContractTests.V0.csproj`.
- Hook `pre-push` roda testes antes do push (`scripts/install-git-hooks.sh`).

## Pontos de entrada no código

| Área | Caminho |
|------|---------|
| Timer / rotação | `src/FunctionApp/Functions/LoteriaLoaderTimerFunction.cs` |
| Caso de uso sync | `src/Application/UseCases/LoteriaResultsUpdateUseCase.cs` |
| Cliente HTTP | `src/Infrastructure/Http/LotodicasApiClient.cs` |
| Scheduler Table | `src/Infrastructure/Storage/AzureTableLoteriasLoaderSchedulerStore.cs` |
| Testes rotação | `tests/ContractTests.V0/LoteriaLoaderTimerRotationTests.cs` |

## Não negociáveis

- **Spec-driven**: nada de “feature” sem citar o recorte do spec.
- **TDD / contrato primeiro**: testes precisam provar o recorte.
- **Determinismo**: mesma entrada canônica ⇒ mesma saída canônica (quando aplicável).
- **Sem defaults ocultos**: ambiguidades devem ser resolvidas no cliente/host (perguntas objetivas), não por inferência silenciosa.
- **Fatias pequenas**: um objetivo por change set.
- **1 tipo público por arquivo**: ver `.cursor/rules/one-type-per-file-and-folders.mdc`.
