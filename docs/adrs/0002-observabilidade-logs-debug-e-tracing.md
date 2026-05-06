---
status: aceito
deciders: [""]
date: "2026-05-06"
tags: ["lotofacil-loader", "observabilidade", "logging", "tracing", "application-insights", "opentelemetry"]
---

# ADR 0002: Observabilidade (logs Debug estruturados + tracing) no Lotofacil-Loader

## Contexto

O recorte V0 do repositório é uma **Azure Function** (Timer Trigger) que chama um caso de uso (`LoteriaResultsUpdateUseCase`) para atualizar resultados via API e persistir artefatos em Blob/Table.

Em produção, quando ocorre um comportamento inesperado (ex.: early-exit em horário/dia incorreto, falha em lacuna, janela expirando, retry/rate-limit), hoje não há instrumentação suficiente para reconstruir **quais passos ocorreram** e **por que parou**.

Fontes de verdade relacionadas:

- `docs/spec-driven-execution-guide.md` (Contrato V0 — seção **14 Observabilidade**: campos mínimos e `reason_stop`)
- `docs/brief.md` (restrições e intenção operacional)
- `docs/adrs/0001-lotofacil-loader-azure-function.md` (trigger fino + observabilidade como responsabilidade da superfície pública)

## Decisão

Adotar um padrão normativo de observabilidade para o loader, composto por:

1) **Logs estruturados** com nível **Debug** em cada etapa relevante (ação/decisão), mantendo:
   - correlação por `run_id` (já existente) e por `trace_id` (via tracing);
   - logs de finalização sempre com `reason_stop` (já existe e permanece o “motivo oficial”).

2) **Tracing por execução** usando `System.Diagnostics.Activity` / `ActivitySource`:
   - uma Activity por execução por modalidade (ex.: Lotofácil e Mega-Sena), com tags padronizadas;
   - eventos (Activity events) para marcar passos importantes e facilitar reconstrução do fluxo;
   - compatibilidade com **Application Insights** e **OpenTelemetry** (OTel), sem acoplar o núcleo a um exporter específico.

3) **Controle de verbosidade via configuração padrão do .NET**:
   - nível de logs controlado por `Logging__LogLevel__*` (ambiente), sem toggle adicional para verbosidade.

> Detalhamento técnico (pontos de instrumentação, nomes de activities, tags, eventos, configuração por ambiente e estratégia de testes): ver `docs/observability.md`.

## Escopo (recorte)

- Instrumentação deve cobrir:
  - Function (orquestração e fechamento do `reason_stop`);
  - caso de uso (decisões e checkpoints);
  - cliente HTTP (tentativas/status/retry-after/retries);
  - persistências (blob/state) ao menos nos “marcos” de leitura/escrita e decisões.
- Não alterar semântica/contrato do loader (somente observabilidade).
- Não introduzir “defaults ocultos”: qualquer configuração relevante deve ser explicitada em docs.

## Consequências

- **Prós**:
  - Investigações em produção passam a ser guiadas por **trace + logs Debug** com correlação.
  - Debug pode ser habilitado seletivamente por categoria via `Logging__LogLevel__...` sem redeploy.
  - `reason_stop` continua sendo o “contrato” de parada; tracing apenas adiciona explicabilidade.
- **Contras / custos**:
  - Logs Debug podem gerar volume se habilitados amplamente (mitigação: `LoggingLevel` por categoria e sampling de traces no App Insights).
  - Exige disciplina para manter mensagens e campos estruturados estáveis (evitar log “texto solto”).

## Regras normativas (checklist de aceitação)

Uma implementação dessa ADR só é considerada pronta quando:

- Existe **uma Activity por execução/modality**, com `trace_id` visível em produção.
- Cada etapa com ação/decisão relevante emite `LogDebug` com campos estruturados.
- Os logs finais continuam emitindo `reason_stop`, e é possível inferir por logs:
  - se guardas de calendário foram aplicadas ou desabilitadas por toggle;
  - qual foi o checkpoint persistido vs. processado;
  - tentativas/retries e motivo (429/5xx/timeout).
- Há um **plano de teste** que valida (sem exporter) a criação de Activities + tags/eventos e a emissão de mensagens-chave (ver `docs/observability.md`).

## Referências

- `docs/observability.md`
- `docs/spec-driven-execution-guide.md`
- `docs/brief.md`

