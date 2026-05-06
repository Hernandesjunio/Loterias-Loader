# Observabilidade (normativo): logs Debug estruturados + traces (Activity) — Lotofacil-Loader

## Objetivo

Este documento define os **detalhes técnicos normativos** para instrumentar o loader com:

- **logs Debug** (em pontos de ação/decisão relevantes); e
- **traces** (via `Activity` / `ActivitySource`) para reconstruir todos os passos de uma execução.

Ele complementa e operacionaliza:

- `docs/adrs/0002-observabilidade-logs-debug-e-tracing.md` (decisão)
- `docs/spec-driven-execution-guide.md` (Contrato V0 — seção 14 Observabilidade: campos e `reason_stop`)

## Princípios (não negociáveis)

- **Sem mudança de semântica**: observabilidade não altera o fluxo (somente mede/explica).
- **Sem segredos em logs**: não logar token, connection strings, SAS, etc.
- **Campos estruturados**: logs devem permitir filtros e correlação (não “texto solto”).
- **Correlacionável**: cada execução deve ser rastreável por `run_id` e por `trace_id`.
- **Controlável por configuração padrão .NET**: habilitar Debug por `Logging__LogLevel__...`, sem toggle extra.

## Glossário rápido

- **run_id**: GUID gerado no trigger para correlacionar tudo naquela rodada do timer.
- **trace**: conjunto de spans/activities correlacionadas (uma “árvore”).
- **span/activity**: unidade de trabalho (no .NET: `Activity`) dentro do trace.
- **tags**: pares chave/valor no span (`Activity.SetTag`) para consulta.
- **events**: marcações pontuais no span (`Activity.AddEvent`) com timestamp e atributos.

## Taxonomia de traces (Activities)

### ActivitySource

- **Name** (normativo): `Lotofacil.Loader`

> A implementação deve criar um `ActivitySource` com esse nome (único) para facilitar filtros.

### Activities (nomes normativos)

Uma Activity **por execução por modalidade**:

- `LotofacilLoader.UpdateResults`

Sub-activities são opcionais; se forem usadas, devem manter o prefixo e semântica clara:

- `LotofacilLoader.Guards`
- `LotofacilLoader.ReadState`
- `LotofacilLoader.ReadBlob`
- `LotofacilLoader.Bootstrap`
- `LotofacilLoader.Incremental`
- `LotofacilLoader.PersistBlob`
- `LotofacilLoader.PersistState`
- `LotofacilLoader.Http`

### Tags (campos normativos do span)

Tags mínimas na Activity raiz `LotofacilLoader.UpdateResults`:

- `run_id` (string)
- `modality` (string; ex.: `lotofacil`, `megasena`)
- `timezone` (string; `America/Sao_Paulo`)
- `deadline_seconds` (int; 180)
- `disable_business_day_guard` (bool)
- `disable_20h_guard` (bool)
- `reason_stop` (string; valor do enum no final)
- `last_loaded_contest_id` (int; quando disponível)
- `latest_id` (int?; quando disponível)
- `processed_count` (int; ao final)
- `persisted_last_id` (int; ao final)
- `retries_count` (int; ao final; total de retries executados pelo ciclo HTTP do loader)
- `rate_limit_wait_seconds_total` (double/int; ao final; soma de esperas por rate limit/Retry-After/pacing, em segundos)
- `elapsed_seconds` (double/int; ao final; duração total da execução, em segundos)

Tags úteis recomendadas (quando custam pouco e não vazam segredos):

- `now_utc` (ISO string) e/ou `today_local` (YYYY-MM-DD) — cuidado para não “explodir cardinalidade” em métricas; para logs é ok, para tags use com parcimônia.
- `guard_business_day_applied` (bool) e `guard_20h_applied` (bool) quando houver early-exit (explica se foi aplicado ou desabilitado).

### Events (marcos normativos)

Registrar `ActivityEvent` (mesmo na Activity raiz) para marcos:

- `guards.evaluate`
  - atributos: `is_business_day`, `has_passed_20h`, `guard_business_day_enabled`, `guard_20h_enabled`
- `state.read.start` / `state.read.ok`
- `blob.read.start` / `blob.read.ok`
- `bootstrap.start` / `bootstrap.ok`
- `latestId.fetch.start` / `latestId.fetch.ok`
- `incremental.loop.start`
- `incremental.id.start` / `incremental.id.ok` (pode ser amostrado para evitar excesso; ver seção “Volume”)
- `persist.blob.start` / `persist.blob.ok`
- `persist.state.start` / `persist.state.ok`
- `stop` (atributos: `reason_stop`)

## Padrão de logs Debug (mensagens + campos)

### Convenções

- **Formato**: logs estruturados (placeholders) com nomes estáveis.
- **Níveis**:
  - `Debug`: passos, decisões, contagens e parâmetros não sensíveis.
  - `Information`: término de execução (`v0_stop`) e eventos operacionais relevantes.
  - `Warning`: paradas seguras por concorrência (ETag / conflito blob) e rate-limit severo.
  - `Error`: falhas duras, exceções não tratadas.

### Campos mínimos em logs

Todos os logs relevantes devem carregar (por template, scope ou por Activity):

- `run_id`
- `modality`
- `trace_id` (quando disponível)

E, quando aplicável, também:

- `reason_stop`
- `deadline_seconds`, `timezone`
- `disable_business_day_guard`, `disable_20h_guard`
- `last_loaded_contest_id`, `latest_id`, `processed_count`, `persisted_last_id`
- `retries_count`, `rate_limit_wait_seconds_total`, `elapsed_seconds`

### Pontos mínimos de Debug no fluxo (normativo)

No caso de uso:

- **Início**: “start execute” com `nowUtc`, `deadlineUtc`, `todayLocal`.
- **Guards**:
  - logar resultado das avaliações (dia útil, passou 20h);
  - logar “early-exit aplicado” vs “guard desabilitada por toggle”.
- **State/Blob**:
  - “read state”/“state missing → init path”;
  - “read blob” e “doc empty → bootstrap”.
- **LatestId**:
  - “fetch latestId” + valor;
  - “already aligned” com `lastLoaded`/`latestId`.
- **Loop incremental**:
  - “processing id” (pode ser amostrado, ou apenas início/fim + contagem);
  - “pacing wait” com segundos;
  - “budget insufficient/window expiring”.
- **Persistência**:
  - start/ok write blob;
  - start/ok write state;
  - sempre registrar “persisted_last_id”.

No HTTP client:

- “http request start” com endpoint lógico (last/byId/all), attempt, timeout.
- “http response” com status code.
- “retry scheduled” com motivo (429 + retry-after, 5xx, timeout) e delay.

Na Function:

- início por modalidade com `run_id`, toggles efetivas, timezone.
- final com `v0_stop` (já existe) + `trace_id` (se possível enriquecer).

## Controle de nível de logs (configuração)

### Padrão .NET (variáveis de ambiente)

O mecanismo principal é o padrão do .NET:

- `Logging__LogLevel__Default`
- `Logging__LogLevel__Microsoft` (normalmente `Warning`)
- `Logging__LogLevel__Lotofacil.Loader` (namespace/assembly do projeto)
- `Logging__LogLevel__Lotofacil.Loader.Application` (se quiser granularidade)
- `Logging__LogLevel__Lotofacil.Loader.Infrastructure`
- `Logging__LogLevel__Lotofacil.Loader.FunctionApp`

Exemplo (produção): habilitar Debug só para o loader:

```text
Logging__LogLevel__Default=Information
Logging__LogLevel__Microsoft=Warning
Logging__LogLevel__Lotofacil.Loader=Debug
```

### Azure Functions (isolated) e host.json

O `host.json` já habilita Application Insights com sampling. Para controle de nível, a prática recomendada é via `Logging__LogLevel__...` (Application Settings) e/ou ajustes adicionais no host (quando aplicável ao worker).

Normativo: **o repositório não deve depender de um valor hardcoded**; o nível vem do ambiente.

### Azure Functions `dotnet-isolated`: habilitar Debug no terminal (worker + host)

Em Azure Functions **`dotnet-isolated`**, existem dois componentes relevantes:

- **Host (Functions runtime/Core Tools)**: aplica filtros para categorias como `Function` (ex.: pode travar em `Information`).
- **Worker (.NET isolated)**: é o processo .NET onde roda o seu código e onde o `ILogger<T>` é emitido.

Normativo (para desenvolvimento local): para garantir que logs `Debug` do seu código apareçam no terminal, o **worker** deve:

- ter um **console logger provider**; e
- aplicar a seção `Logging` da configuração (.NET) no pipeline de logging.

Exemplo canônico (worker), a ser colocado no `Program.cs` da FunctionApp:

```csharp
.ConfigureLogging((loggingContext, loggingBuilder) =>
{
    loggingBuilder.AddJsonConsole(consoleOptions =>
    {
        consoleOptions.IncludeScopes = true;
    });

    loggingBuilder.AddConfiguration(loggingContext.Configuration.GetSection("Logging"));
})
```

E o nível deve ser controlado **via ambiente** (ex.: `Logging__LogLevel__Lotofacil.Loader=Debug`).

### Volume e amostragem (guideline)

- Logs Debug podem ser muito verbosos no loop incremental. Estratégias permitidas:
  - Logar Debug por `id` apenas nos primeiros N e nos últimos N; ou
  - logar Debug por `id` somente quando houver retry/erro; ou
  - logar um resumo por bloco (ex.: “processed 7 ids”).
- Traces em Application Insights podem estar sob sampling. Por isso, os logs finais `v0_stop` (Information) devem permanecer.

## Integração com Application Insights e OpenTelemetry

### Expectativa

A instrumentação deve ser “exporter-agnostic”:

- a aplicação cria `Activity`/tags/events;
- o ambiente decide se exporta para Application Insights, OTel Collector (OTLP), ambos ou nenhum.

### Configuração típica (sem hardcode)

- **Application Insights**: via variável de ambiente `APPLICATIONINSIGHTS_CONNECTION_STRING` (ou equivalente configurado no Azure).
- **OpenTelemetry OTLP**: via variáveis `OTEL_EXPORTER_OTLP_ENDPOINT` e correlatas.

> Este documento não fixa bibliotecas/pacotes; ele fixa a semântica de tracing e os campos normativos. A implementação pode escolher os packages mínimos do runtime da Function, desde que mantenha a compatibilidade.

## Estratégia de testes (para provar que “está funcionando” sem exporter)

### 1) Teste de tracing (unitário, sem rede)

Objetivo: validar que a Activity é criada e contém tags/eventos normativos.

Abordagem:

- registrar um `ActivityListener` no teste;
- iniciar a execução (chamando o caso de uso / entrypoint de contrato com fakes);
- capturar Activities iniciadas e verificar:
  - nome `LotofacilLoader.UpdateResults`;
  - presença de tags mínimas (ex.: `run_id`, `modality`, `reason_stop` ao final);
  - presença de eventos críticos (ex.: `guards.evaluate`, `stop`).

Notas:

- Não depende de App Insights/OTel.
- Deve ser determinístico (clock fake).

### 2) Teste de logs Debug (unitário)

Objetivo: validar que logs Debug são emitidos nos pontos principais.

Abordagem:

- usar um `ILoggerProvider` em memória (test double) para capturar mensagens/níveis/propriedades;
- executar um cenário pequeno (ex.: early-exit) e afirmar que:
  - existe log Debug de avaliação das guards;
  - existe log Debug/Info final com `reason_stop`.

### 3) Teste de integração (opcional; “smoke”)

Objetivo: validar correlação em runtime real (Function host), se/quando fizer sentido.

Abordagem:

- executar Function local com `Logging__LogLevel__Lotofacil.Loader=Debug`;
- validar que logs contêm `run_id` e que há `trace_id` quando o tracing estiver habilitado.

## Checklist de implementação (resumo)

- [ ] ActivitySource `Lotofacil.Loader`
- [ ] Activity raiz `LotofacilLoader.UpdateResults` por execução/modality
- [ ] tags mínimas + `reason_stop` ao final
- [ ] events normativos (guards/state/blob/http/persist/stop)
- [ ] logs Debug com campos estruturados por etapa
- [ ] controle de nível por `Logging__LogLevel__...`
- [ ] testes unitários para Activity + logs

