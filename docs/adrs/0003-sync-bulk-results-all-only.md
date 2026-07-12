---
status: aceito
deciders: [""]
date: "2026-07-12"
tags: ["lotofacil-loader", "breaking-change", "lotodicas", "api"]
---

# ADR 0003: Sync exclusivo via `/results/all`

## Contexto

O provedor Lotodicas cobra **por request**, não por volume de dados. O fluxo incremental V0 (`/results/last` + `/results/{id}`) gerava N+1 requests por tick quando havia lacunas, pacing de 10s entre chamadas, falhas 404 em ids intermediários e timeouts no host Azure.

## Decisão

A partir desta ADR, **todas as modalidades** sincronizam **exclusivamente** via:

`GET /api/v2/{lotteryApiSegment}/results/all?token=<TOKEN>`

**Uma execução = um request HTTP** por modalidade (independente do estado do blob).

## Removido (breaking)

- Endpoints `/results/last` e `/results/{id}` no client e no use case.
- Loop incremental de lacunas e pacing de 10s entre requests.
- Distinção bootstrap vs incremental.
- Early-exits que evitam chamada à API (`EARLY_EXIT_ALREADY_LOADED_TODAY`, `EARLY_EXIT_ALREADY_ALIGNED`).
- `ReasonStop` relacionados a early-exit acima.

## Mantido

- Janela de execução 180s e `IExecutionBudget` no client HTTP.
- Rotação de modalidade por tick (1 modalidade por invocação, default).
- Persistência **blob primeiro**, **Table depois**.
- ETag / concorrência otimista.
- Retry 429/5xx/timeout no único request (capado pelo budget).
- Guard `HARD_FAIL_STATE_INCONSISTENT_TABLE_GT_BLOB`.

## Consequências

- **Prós**: 1 request/tick/modalidade; elimina 404 em lacuna; execução mais rápida e previsível.
- **Contras**: payload grande (~3700+ concursos Lotofácil) por request; reescrita completa do blob a cada tick; testes e spec incrementais obsoletos.

## Referências

- `docs/spec-driven-execution-guide.md` §6, §11, §12 (atualizados)
- ADR 0001 (ordem blob→table)
