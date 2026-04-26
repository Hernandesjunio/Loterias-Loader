# Baseline normativo (V0): fronteiras, contrato, determinismo e não-objetivos

## Objetivo desta nota

Congelar um **checkpoint de alinhamento normativo** para a V0 do repositório, deixando explícito e rastreável:

- as **fronteiras** do sistema (o que é “núcleo” vs. “superfície pública” vs. “infra/IO”);
- onde vive o **contrato público** (o que é normativo vs. contexto);
- as regras de **determinismo/reprodutibilidade** (o que impede defaults ocultos);
- os **não-objetivos** (o que não será inferido nem prometido).

Esta nota **não** introduz features; ela apenas reduz ambiguidade e evita contradições silenciosas.

## Fontes de verdade (ordem prática)

1. `docs/spec-driven-execution-guide.md` — seção **Contrato V0 — Lotofacil Loader (normativo)** (**normativo**)
2. `docs/adrs/0001-lotofacil-loader-azure-function.md` — decisões de arquitetura/recorte (**alinhado ao contrato**)
3. `docs/project-guide.md` — regras de fronteira e recorte do projeto (**fronteiras**)

Regra: se houver conflito entre documentos, o **Contrato V0 (normativo)** prevalece para semântica/contrato de execução.

## Fronteiras (normativas)

Baseado em `docs/project-guide.md`:

- **Núcleo de semântica**:
  - não depende de transporte (API/CLI) nem de IO concreto;
  - contém regras de encerramento antecipado, janela, idempotência e montagem do artefato canônico.
- **Superfície pública**:
  - no recorte V0, é o **entry point** (Azure Function com Timer Trigger);
  - valida/parsa/configura e chama caso de uso; **não** implementa cálculo do domínio.
- **Infra/IO e integrações**:
  - API externa + Storage (Blob/Table) + serialização + relógio/timezone (quando aplicável);
  - não definem semântica; apenas materializam leituras/escritas e primitivas técnicas.

## Contrato público (o que é normativo na V0)

O contrato público observado para a V0 é o **Contrato V0 — Lotofacil Loader (normativo)** em `docs/spec-driven-execution-guide.md`, que fixa, entre outros:

- **Timezone e regras de calendário** (dia útil e “20h”) para impedir defaults implícitos.
- **Agendamento (CRON)**.
- **Janela máxima** de execução e encerramento seguro (retomada no próximo tick).
- **Entradas canônicas** via variáveis de ambiente (nomes e obrigatoriedade).
- **Saídas canônicas**:
  - documento JSON no blob `Lotofacil` (formato e invariantes);
  - state mínimo no Table (chaves/campos e concorrência por ETag).
- **Regras de idempotência/concorrência** e **ordem de persistência** (blob primeiro, table depois).
- **Resiliência/rate limit/pacing** (incluindo precedência de `Retry-After` e teto pela janela).

Observação: o ADR `0001` deve ser lido como decisão de recorte/arquitetura; ele **não substitui** o contrato normativo.

## Determinismo e reprodutibilidade (normativas)

- **Sem defaults ocultos**: decisões que afetam “o que acontece” devem estar explicitadas no Contrato V0 (ex.: timezone, CRON, regra do 20h, dia útil, invariantes do blob, política de conflito por ETag).
- **Mesma entrada canônica ⇒ mesma saída canônica** para o artefato do blob, dentro do contrato V0.
- **Reexecução é esperada**:
  - idempotência por `contest_id` (dedupe/sobrescrita do item);
  - checkpoint avança somente quando o blob persistido reflete o último concurso processado.
- **Encerramentos antecipados** e expiração de janela são “paradas seguras” (não implicam side-effects parciais fora do contrato).

## Não-objetivos (anti-objetivos) deste repositório/recorte V0

- **Não** fazer promessas preditivas (“melhorar chances”, “prever resultados”, etc.).
- **Não** usar inferência para preencher lacunas do contrato (contrato incompleto = fatia não pronta).
- **Não** embutir “defaults” silenciosos em runtime (ex.: timezone “local”, container padrão, política de feriados) fora do contrato.
- **Não** implementar consumo externo do blob via SAS neste repositório (fora do escopo do loader).
- **Não** introduzir metadados/versionamento no blob além do que está normatizado no Contrato V0 (qualquer mudança desse tipo é mudança de contrato e exige docs+testes+código juntos).

## Check de consistência (este checkpoint)

- A lista de “decisões em aberto” do ADR `0001` foi normalizada para **não conflitar** com o Contrato V0: itens essenciais já estão fechados no contrato e não devem ser reabertos por inferência.
- Se surgir uma decisão que **não** está no Contrato V0 e impacta semântica/contrato/métricas, registrar em **ADR novo** antes de qualquer implementação.

