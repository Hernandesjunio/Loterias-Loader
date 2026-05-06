# Templates atômicos de execução (portátil)

Estes templates são pedidos **copy/paste** para desenvolvimento assistido por IA. Mantenha-os atômicos: **um objetivo, uma prova**.

Regra: cada template deve citar a **fatia** do spec que está sendo materializada e o **teste** que prova o comportamento.

## Coerência com `docs/spec-driven-execution-guide.md` (como ler)

Estes templates são uma forma operacional (copy/paste) do que o guia define como ordem recomendada de trabalho e como contrato mínimo.

- **Fonte normativa**: `docs/spec-driven-execution-guide.md`
- **Gate não negociável**: **não avance para as fases 2+** sem o **“Contrato V0 — Lotofacil Loader (normativo)”** estar fechado no guia.
- **Seções do guia que estes templates materializam**:
  - “Contrato mínimo que toda fatia deve explicitar”
  - “Anti-alucinação: decisões que o contrato da V0 DEVE fixar”
  - “Ordem recomendada de trabalho (passo a passo, completo)”

## Template — Pedido atômico (base)

```md
Objetivo (único):
- <descreva UMA mudança>

Fatia do spec que estou materializando:
- <citar doc e seção/recorte>

Referências obrigatórias:
- <docs/...>
- <docs/...>

Arquivos que podem ser alterados (mínimo possível):
- <arquivo A>
- <arquivo B>

Restrições:
- não extrapolar além do recorte citado;
- manter TDD (teste primeiro);
- respeitar fronteiras de arquitetura;
- sem defaults ocultos (tudo explícito por contrato/config);
- manter determinismo quando aplicável;
- não introduzir segredos em código.

Critério de pronto (prova):
- <qual teste prova?>
- <quais casos devem estar cobertos?>
```

## Fase 0 — Congelar baseline (sem feature)

```md
Implemente apenas um checkpoint de alinhamento normativo (sem codar feature): fronteiras, contrato, determinismo e não-objetivos.

Referências obrigatórias:
- docs/project-guide.md
- docs/spec-driven-execution-guide.md
- docs/adrs/* (quando houver ADRs relevantes para a fatia)

Arquivos esperados:
- docs/<nota curta de baseline>.md (se necessário)
- docs/adrs/<novo-adr>.md (somente se decisão estiver faltando)

Critério de pronto:
- baseline explícito e rastreável; nenhuma contradição crítica escondida.
```

## Fase 1 — Contrato normativo da V0 (documento único)

```md
Implemente apenas a atualização do **Contrato V0 (normativo)** dentro de `docs/spec-driven-execution-guide.md` (sem implementar código).

O contrato deve explicitar:
- Entradas canônicas (configs/variáveis de ambiente; obrigatórias; validações)
- Feature toggles operacionais (quais travas/regras podem ser desabilitadas por config; defaults explícitos)
- Saídas canônicas (artefatos; formato; invariantes)
- Regras e encerramentos antecipados
- Limites e janelas (timeout, janela máxima, rate limit / retry)
- Idempotência/concorrência
- Observabilidade (logs/métricas e motivos de parada)

Checklist anti-alucinação (obrigatório fixar no contrato):
- timezone (qual e como é configurada) e como derivar “hoje”
- definição de “dia útil” (segunda–sexta? feriados? fonte do calendário)
- regra do “20h” (comparação exata)
- feature toggles para travas de calendário (nomes de config; defaults; efeito em encerramentos antecipados)
- expressão CRON final (Azure Functions com segundos)
- primeira execução (state ausente) e resolução de inconsistências table vs blob
- invariantes do blob (ordem, dedupe, sobrescrita, serialização canônica)
- classificação de erros (falha dura vs parada segura) e tratamento de lacunas com erro/404
- precedência e parâmetros de rate limit / retry / pacing (Retry-After vs pacing mínimo de 10s; timeouts; intervalo; limite)
- comportamento em conflito de concorrência (ETag/table e corrida no blob)
- regra de checkpoint: state só avança após blob refletir o último concurso persistido

Referências obrigatórias:
- docs/spec-driven-execution-guide.md
- docs/adrs/* (decisões arquiteturais relevantes)

Arquivos esperados:
- docs/spec-driven-execution-guide.md (seção “Contrato V0 — Lotofacil Loader (normativo)”)

Critério de pronto:
- o contrato permite escrever testes de contrato sem “inventar” comportamentos.
```

## Fase incremental — Feature toggles para travas de calendário (dia útil / 20h)

```md
Implemente apenas a fatia “feature toggles para travas de calendário”:
- permitir desligar, por configuração, as verificações de:
  - “hoje é dia útil?” (permite executar no fim de semana)
  - “já passou das 20h?” (permite executar antes do horário)

Fatia do spec que estou materializando:
- docs/spec-driven-execution-guide.md
  - seção 2 (entradas canônicas: feature toggles)
  - seção 10 (encerramentos antecipados condicionais às toggles)
- docs/brief.md (restrições e configuração: toggles documentadas)

Plano de teste (prova):
- adicionar/atualizar testes de contrato para provar:
  - com toggles desligadas (default), mantém comportamento padrão (dia útil + após 20h);
  - com `DisableBusinessDayGuard=true`, não encerra no fim de semana;
  - com `Disable20hGuard=true`, não encerra antes das 20h;
  - toggles são independentes (combinações possíveis).

Arquivos que podem ser alterados (mínimo possível):
- docs/spec-driven-execution-guide.md
- docs/brief.md
- tests/** (fixtures + testes)
- src/Application/UseCases/LoteriaResultsUpdateUseCase.cs (aplicar toggles nos early exits)
- src/**/Options/*.cs e wiring de DI/config (se necessário)

Restrições:
- defaults explícitos (sem inferência): toggles devem ter default normativo e comportamento documentado;
- toggles não podem mudar timezone, janela, ou contrato do blob/state; apenas desligar travas de calendário;
- observabilidade deve registrar o mesmo `reason_stop`, e deve ser possível inferir por logs se a trava foi aplicada ou desabilitada por toggle.

Critério de pronto:
- docs atualizadas e testes provam os quatro cenários mínimos.
```

## Fase incremental — Observabilidade (logs Debug estruturados + tracing)

```md
Implemente apenas a fatia “observabilidade (logs Debug estruturados + tracing)”:
- adicionar logs **Debug** em cada ponto com ação/decisão relevante no fluxo de atualização (use case, HTTP e superfície pública);
- adicionar **traces** por execução/modality usando `Activity`/`ActivitySource`, com tags/events normativos;
- garantir correlação por `run_id` e `trace_id` (quando habilitado), sem alterar semântica/contrato do loader.

Fatia do spec que estou materializando:
- docs/spec-driven-execution-guide.md
  - seção 14 (Observabilidade — campos mínimos e `reason_stop`)
- docs/adrs/0002-observabilidade-logs-debug-e-tracing.md (decisão)
- docs/observability.md (detalhes técnicos normativos: activities, tags, events, testes, config)
- docs/brief.md (seção “Observabilidade (logs e traces)”)

Plano de teste (prova):
- adicionar testes unitários para provar, sem exporter:
  - existe Activity `LotofacilLoader.UpdateResults` via `ActivitySource` `Lotofacil.Loader`;
  - tags mínimas são preenchidas no final (inclui `reason_stop`, `retries_count`, `rate_limit_wait_seconds_total`, `elapsed_seconds`);
  - eventos mínimos existem (`guards.evaluate`, `stop`);
  - logs Debug são emitidos nos marcos principais (ex.: avaliação de guards e stop).

Arquivos que podem ser alterados (mínimo possível):
- docs/brief.md (seção Observabilidade; referências)
- docs/spec-driven-execution-guide.md (somente para referenciar ADR/guia técnico, sem mudar contrato)
- docs/fases-execucao-templates.md (este template)
- docs/adrs/0002-observabilidade-logs-debug-e-tracing.md (se precisar ajustes de decisão)
- docs/observability.md (se precisar ajuste técnico normativo)
- tests/** (novos testes unitários de observabilidade)
- src/Application/** (instrumentação no caso de uso: `ILogger` + Activity events/tags)
- src/Infrastructure/Http/** (instrumentação no HTTP client: tentativas/status/retry)
- src/FunctionApp/** (instrumentação no trigger e wiring para export opcional)

Restrições:
- sem mudança de semântica/contrato: `reason_stop` permanece o motivo oficial de parada;
- sem segredos em logs (não logar token, connection string, etc.);
- controle de verbosidade via `Logging__LogLevel__...` (padrão .NET), sem toggle adicional;
- evitar excesso de volume: permitir amostragem de logs Debug no loop incremental (ver `docs/observability.md`).

Critério de pronto:
- docs + testes + código alinhados;
- é possível reconstruir o passo-a-passo de uma execução em produção com `run_id` + `trace_id`;
- testes unitários provam que tracing/logging básicos existem e carregam campos normativos.
```

## Fase 2 — Fixtures + goldens determinísticos (V0)

```md
Implemente apenas fixtures e goldens determinísticos para a V0, sem código de produção.

Cobertura mínima:
- fixtures de API (último; por id; 200; 429 com Retry-After; 5xx; timeout)
- fixtures de calendário (não útil; útil antes das 20h; útil após as 20h)
- fixtures de storage (state inexistente; state existente; conflito de ETag)
- golden do blob (documento `{ draws: [...] }` para sequência pequena)

Referências obrigatórias:
- docs/spec-driven-execution-guide.md

Arquivos esperados:
- tests/fixtures/api/*.json
- tests/fixtures/calendar/*.json
- tests/fixtures/storage/*.json
- tests/goldens/blob/*.json

Critério de pronto:
- fixtures/goldens são pequenos, legíveis e estáveis (sem timestamps variáveis).
```

## Fase 3 — Testes de contrato (vermelhos primeiro)

```md
Implemente apenas os testes de contrato da V0 (primeiro vermelhos), sem implementar a lógica de produção.

Casos mínimos a provar:
- encerramentos antecipados (não útil; antes das 20h; já carregou sorteio do dia)
- alinhamento: `latestId <= lastLoaded` não faz downloads por id nem persistência
- lacunas: baixa `lastLoaded+1..latestId` em ordem e respeita janela de execução
- resiliência: 429 respeita Retry-After; retry limitado ao que cabe na janela
- persistência: blob antes de table; falha no blob não atualiza table; conflito de ETag é seguro

Referências obrigatórias:
- docs/spec-driven-execution-guide.md

Arquivos esperados:
- tests/<ContractTests>/*

Critério de pronto:
- testes falham pelo motivo correto e descrevem o comportamento esperado (mensagens claras).
```

## Fase 4 — Esqueleto mínimo que compila e roda testes

```md
Implemente apenas o esqueleto mínimo para compilar e rodar testes (sem funcionalidade completa).

Inclui:
- projeto(s) e estrutura mínima
- DI mínima e separação por camadas (se o projeto tiver camadas; caso contrário, declarar explicitamente “monólito”)
- interfaces (portas) no núcleo, sem adaptadores reais ainda

Referências obrigatórias:
- docs/project-guide.md
- docs/adrs/*
- docs/spec-driven-execution-guide.md

Critério de pronto:
- build e testes rodam (mesmo que os testes de contrato ainda estejam vermelhos).
```

## Fase 5 — Núcleo semântico mínimo (fazer passar testes)

```md
Implemente apenas o núcleo semântico necessário para fazer os testes passarem.

Inclui:
- modelos do domínio e documento do blob
- caso de uso de atualização (orquestração do fluxo: estado → último → lacunas → janela → persistência)

Referências obrigatórias:
- docs/spec-driven-execution-guide.md

Critério de pronto:
- testes de contrato passam usando doubles/fakes (sem depender de Azure real).
```

## Fase 6 — Infraestrutura (adaptadores reais)

```md
Implemente apenas os adaptadores reais (infra) para cumprir o contrato, sem alterar semântica.

Inclui:
- cliente da API com config (baseUrl + token) e políticas de resiliência (429/Retry-After, timeout, retry)
- blob store (Content-Type correto; escrita coerente do documento completo)
- table store (ETag; leitura/gravação de state)

Referências obrigatórias:
- docs/spec-driven-execution-guide.md
- docs/adrs/*

Critério de pronto:
- testes de contrato continuam passando; testes de integração (se existirem) validam IO real.
```

## Fase 6.5 — Superfície pública (Azure Function Timer Trigger)

```md
Implemente apenas a **superfície pública** do recorte V0: uma **Azure Function** com **Timer Trigger** que orquestra a execução do caso de uso do núcleo (trigger fino), sem duplicar semântica.

Inclui (mínimo necessário):
- criar o projeto/pasta da Function (ex.: `src/FunctionApp/` ou nome explicitado no repositório);
- adicionar `host.json` no projeto da Function;
- implementar Timer Trigger com CRON **normativo** `0 0 * * * *`;
- ler e validar **todas** as variáveis de ambiente obrigatórias do Contrato V0 (falha dura sem efeitos quando inválidas);
- wiring de DI para registrar adaptadores reais (HTTP API client, Blob store, Table store);
- logging estruturado de **motivo de parada** (`reason_stop`) conforme seção de Observabilidade do Contrato V0.

Referências obrigatórias:
- docs/spec-driven-execution-guide.md (Contrato V0 — seções 1, 2, 4 e 14)
- docs/adrs/0001-lotofacil-loader-azure-function.md
- docs/project-guide.md

Arquivos esperados (exemplos):
- src/FunctionApp/**/*
- src/FunctionApp/host.json

Critério de pronto (prova, sem ambiguidade):
- o Timer Trigger existe e compila no projeto da Function;
- a Function chama o caso de uso do núcleo e permanece “fina” (sem regras de domínio no handler);
- execução local/deploy é possível apenas com as variáveis de ambiente do contrato (sem inferência/defaults ocultos);
- suíte de testes existente continua passando.
```

> Nota incremental: se o recorte adotado for V0.1+, o schedule do Timer Trigger deve ser resolvido por configuração (ver `docs/spec-driven-execution-guide.md`, seção 4.1). Esta fase permanece válida; o adendo apenas altera a **fonte** do CRON.

## Fase 7 — Evidência e fechamento da V0

```md
Implemente apenas o fechamento por evidência (sem features novas).

Inclui:
- rodar suítes e registrar evidências (o que foi validado)
- checklist de “sem defaults ocultos” (timezone/config/rate limit explícitos)
- validação de idempotência e retomada após expirar janela

Referências obrigatórias:
- docs/test-plan.md (se aplicável)
- docs/spec-driven-execution-guide.md

Critério de pronto:
- existe evidência rastreável do comportamento V0 conforme contrato.
```

## Fase incremental — Bootstrap (carga inicial) via `/results/all` quando blob ausente/vazio

```md
Implemente apenas a fatia “carga inicial (bulk) via `/results/all`”:
- se o blob da modalidade **não existir** ou existir com `draws` **vazio**, executar bootstrap chamando `/api/v2/{lotteryApiSegment}/results/all?token=<TOKEN>`;
- persistir **blob primeiro** (documento completo) e então atualizar o state no Table para `max(contest_id)`;
- após bootstrap, a atualização incremental continua usando `/results/last` + `/results/{id}` para novos concursos.

Fatia do spec que estou materializando:
- docs/spec-driven-execution-guide.md
  - seção 6 (endpoints normativos; inclui `/results/all`)
  - seção 9 (primeira execução / blob ausente/vazio ⇒ bootstrap)
  - seção 11 (algoritmo normativo; passo 2.1)
- docs/brief.md (escopo: carga inicial via `/results/all`)

Plano de teste (prova):
- docs/test-plan.md (casos B2.1, B2.2 e B2.3)

Arquivos que podem ser alterados (mínimo possível):
- docs/spec-driven-execution-guide.md (somente se precisar alinhar contrato)
- docs/test-plan.md (somente se precisar alinhar plano de teste)
- tests/** (fixtures + testes para `/results/all` e bootstrap)
- src/Infrastructure/Http/LotodicasApiClient.cs (novo método para `/results/all`, parametrizado por `lotteryApiSegment`)
- src/Application/UseCases/LoteriaResultsUpdateUseCase.cs (ramo de bootstrap quando blob ausente/vazio)

Restrições:
- não inventar paginação: `/results/all` é sem paginação e retorna todos os concursos desde o primeiro;
- critério de bootstrap: “blob inexistente OU `draws` vazio” (não extrapolar);
- respeitar o contrato: persistência **blob → table**; sem defaults ocultos; determinismo do documento canônico;
- após bootstrap, não usar `/results/all` quando o blob já contiver `draws`.

Critério de pronto:
- testes provam:
  - blob inexistente ⇒ chama `/results/all` e materializa o documento;
  - blob com `draws: []` ⇒ chama `/results/all`;
  - blob com `draws` não vazio ⇒ não chama `/results/all` e segue incremental.
```

## Fase 8 — Teste de integração “ponta a ponta” controlado (sem Docker, quando possível)

```md
Implemente apenas a suíte de teste de integração que executa o fluxo completo da Function (trigger → caso de uso → persistências),
mantendo determinismo e isolando o terceiro (Lotodicas) como Fake.

Objetivo (único):
- validar o processo como um todo, com dependências controladas e reprodutíveis.

Fatia do spec que estou materializando:
- docs/spec-driven-execution-guide.md (Contrato V0 + Adendo V0.1 quando aplicável)
- docs/test-plan.md (seção de integração controlada)

Dependências (regra):
- Lotodicas (terceiro): SEMPRE Fake (servidor HTTP local com respostas determinísticas para:
  - `/results/last`
  - `/results/{id}`
)
- Storage: preferir emulador local (Azurite) quando disponível sem Docker.

Pré-condições (seed determinístico):
- popular Table Storage com o state inicial necessário ao cenário (ex.: `LastLoadedContestId = N`)
- opcionalmente popular o blob `Lotofacil` (quando o cenário cobrir “bootstrap por blob”)

Critérios de pronto (prova):
- o teste executa o fluxo completo com entradas/saídas determinísticas
- os asserts validam:
  - chamadas esperadas ao Fake do Lotodicas (endpoints e parâmetros)
  - conteúdo do blob final (golden ou comparação canônica)
  - state final no Table (checkpoint consistente e ordem blob→table)
```

## Fase 8.1 — Opcional: ambiente de integração via Docker (quando necessário para qualidade)

```md
Implemente apenas a infraestrutura de testes para subir dependências via Docker quando não houver alternativa de qualidade sem Docker.

Escopo típico:
- Azurite em container (Blob + Table), com portas fixas e data dir isolado por execução de teste.

Restrições:
- o teste deve continuar determinístico (ambiente limpo a cada execução)
- a suite deve ser executável localmente e replicável em CI no futuro

Critério de pronto:
- `dotnet test` (ou equivalente) sobe/usa a dependência e finaliza limpando recursos (sem “lixo” persistente)
```

