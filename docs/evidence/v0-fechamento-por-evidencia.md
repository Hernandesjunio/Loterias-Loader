# Evidência rastreável — V0 (fechamento por evidência)

Este documento registra **o que foi validado**, **como foi validado** e **qual evidência** sustenta o comportamento V0 **conforme contrato**.

Referências obrigatórias:

- `docs/spec-driven-execution-guide.md` (seção **Contrato V0 — Lotofacil Loader (normativo)**)
- `docs/test-plan.md`

## Ambiente (execução local)

- **OS**: Windows (win32 10.0.22631)
- **.NET**: `dotnet --version` (use o SDK instalado na máquina)

## O que foi validado (suíte de contrato V0)

Fonte da evidência: `tests/ContractTests.V0/V0ContractBehaviorTests.cs`.

- **Encerramentos antecipados (Contrato V0, seção 10)**:
  - não-dia-útil ⇒ não chama API e não persiste
  - antes das 20h (timezone de referência) ⇒ não chama API e não persiste
  - após 20h e `LastLoadedDrawDate == todayLocal` ⇒ não chama API e não persiste
- **Alinhamento (Contrato V0, seções 10–11)**:
  - `latestId <= lastLoaded` ⇒ chama `/last` uma vez e encerra sem persistências
- **Persistência (Contrato V0, seção 13)**:
  - **ordem obrigatória**: grava **blob primeiro** e **state depois**
- **Janela + retomada (Contrato V0, seções 5 e 11)**:
  - quando a janela expira, a execução **para com segurança** após persistir somente o último id contíguo concluído
  - em execução seguinte, o processamento **retoma** a partir do checkpoint persistido
- **Idempotência (Contrato V0, seção 13)**:
  - reexecução alinhada não reescreve blob/state

## Como rodar (comandos) + output (evidência bruta)

Comando executado:

```bash
dotnet test "c:/_projeto/Lotofacil-Loader/Lotofacil.Loader.slnx" -c Release
```

Output observado (trecho relevante):

```text
Passed!  - Failed:     0, Passed:     9, Skipped:     0, Total:     9, Duration: 89 ms - ContractTests.V0.dll (net8.0)
```

## Checklist — “sem defaults ocultos” (timezone/config/rate limit explícitos)

Este checklist aponta **onde** o contrato fixa as decisões e **onde** isso aparece no código.

- **Timezone explícita (sem “localtime” implícito)**:
  - **Contrato**: `docs/spec-driven-execution-guide.md` → “Timezone de referência: `America/Sao_Paulo`” + regra de derivação de `todayLocal`
  - **Código**: `src/Application/UseCases/UpdateLotofacilResultsUseCase.cs` → `ConvertToSaoPaulo(...)` (IANA `America/Sao_Paulo` com fallback Windows `E. South America Standard Time`)
- **Config explícita (sem segredos/valores hardcoded ocultos)**:
  - **Contrato**: `docs/spec-driven-execution-guide.md` → seção “Entradas canônicas (config/ambiente)”
  - **Código (shape do contrato de config)**:
    - `src/Infrastructure/Options/LotodicasOptions.cs` (`Lotodicas__BaseUrl`, `Lotodicas__Token` via binding)
    - `src/Infrastructure/Options/StorageOptions.cs` (`Storage__ConnectionString`, `Storage__BlobContainer`, `Storage__LotofacilBlobName`, `Storage__LotofacilStateTable`)
- **Rate limit / retry explícitos (sem inferência)**:
  - **Contrato**: `docs/spec-driven-execution-guide.md` → seção 12 (precedência `Retry-After` > 1 req/min > 30s)
  - **Código**:
    - pacing **1 req/min** (60s entre inícios): `src/Application/UseCases/UpdateLotofacilResultsUseCase.cs`
    - retry/timeout por tentativa + `Retry-After` (quando 429): `src/Infrastructure/Http/LotodicasApiClient.cs`
- **Janela/timeout explícitos**:
  - **Contrato**: `docs/spec-driven-execution-guide.md` → seção 5 (janela 180s; orçamento mínimo 15s; timeout 10s)
  - **Código**:
    - janela + orçamento mínimo: `src/Application/UseCases/UpdateLotofacilResultsUseCase.cs`
    - timeout por tentativa: `src/Infrastructure/Http/LotodicasApiClient.cs`

## Observação importante sobre a evidência

Os testes aqui são **determinísticos** e não acessam Azure/API real: a evidência é do **comportamento do núcleo V0** (caso de uso) via `EntryPoint` com portas fake, conforme o princípio spec-driven do repositório.

