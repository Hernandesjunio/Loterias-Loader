---
status: aceito
deciders: [""]
date: "2026-04-25"
tags: ["lotofacil-loader", "azure-functions", "storage", "determinismo"]
---

# ADR 0001: Arquitetura e decisões iniciais do Lotofacil-Loader (Azure Function)

## Contexto

O sistema no recorte atual é uma **Azure Function** (C#/.NET) com **Timer Trigger** para atualizar resultados da Lotofácil via API e persistir:

- **Blob Storage**: um documento JSON (blob `Lotofacil`) para consumo externo via SAS (consumo fora do escopo do loader).
- **Table Storage**: estado mínimo (“último concurso carregado”) para retomar progresso e evitar trabalho redundante.

Fontes de verdade (normativas) para este recorte:

- este ADR (`docs/adrs/0001-lotofacil-loader-azure-function.md`)
- `docs/spec-driven-execution-guide.md` (seção **Contrato V0 — Lotofacil Loader (normativo)**)
- `docs/fases-execucao-templates.md`

## Decisão (V0)

Adotar uma arquitetura em camadas/ports & adapters dentro de **um único Function App**, com:

- Trigger fino (somente orquestração + observabilidade).
- Caso de uso central (ex.: “atualizar resultados”) isolando regras de encerramento antecipado e o loop de preenchimento de lacunas.
- Portas para API, Blob e Table, com adaptadores em infraestrutura.
- Resiliência com Polly e respeito a rate limiting (incluindo 429/`Retry-After` quando presente).
- Persistência em ordem: **blob primeiro**, **estado depois**, para evitar marcar progresso sem materializar o artefato.

## Consequências

- **Prós**:
  - Regras do domínio/testes não ficam acoplados ao SDK do Azure nem ao Timer Trigger.
  - Facilita testes determinísticos (tempo e timezone podem ser abstraídos).
  - Retomada do processamento por lacunas fica explícita (state store).
- **Contras / custos**:
  - Mais “arquitetura” no início (mais arquivos e DI).
  - Exige disciplina para não “vazar” IO e config para dentro do caso de uso.

## Decisões em aberto (fora da V0)

### 1) Decisões do contrato V0 que já foram fechadas (normativas)

Os itens abaixo foram inicialmente listados como “em aberto” na fase de contexto, mas **já estão fixados normativamente** na seção **Contrato V0 — Lotofacil Loader (normativo)** em `docs/spec-driven-execution-guide.md` e, portanto, **não devem** ser reabertos por inferência na implementação:

- **Timezone / dia útil / regra das 20h**: `America/Sao_Paulo`, “dia útil” = seg–sex, e “passou das 20h” = hora local \(>= 20:00:00\).
- **Agendamento (CRON)**: expressão e expectativa de execução definidas.
- **Acesso ao Storage (V0)**: via `Storage__ConnectionString` por ambiente.
- **Container / nomes de recursos**: `Storage__BlobContainer`, `Storage__LotofacilBlobName`, `Storage__LotofacilStateTable` por ambiente.
- **Rate limit / retry / pacing**: precedência de `Retry-After`, intervalo quando ausente e teto pela janela de 3 minutos.
- **Formato/invariantes do blob**: ordenação/deduplicação/idempotência e escrita coerente.
- **Concorrência**: ETag no Table e comportamento em conflito (encerramento seguro; retoma no próximo tick).

### 2) Decisões realmente fora da V0 (ainda não normativas)

Qualquer decisão adicional deve virar um ADR novo **apenas se** alterar semântica/contrato/métricas ou introduzir trade-offs operacionais além do que já está normatizado no Contrato V0. Exemplos típicos (se/quando existirem): “bootstrap histórico” fora do timer, Managed Identity + RBAC, lock explícito (blob lease), metadados/versionamento no blob.

## Referências

- `docs/spec-driven-execution-guide.md`
- `docs/fases-execucao-templates.md`

