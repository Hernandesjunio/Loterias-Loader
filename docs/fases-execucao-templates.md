# Templates atômicos de execução (portátil)

Estes templates são pedidos **copy/paste** para desenvolvimento assistido por IA. Mantenha-os atômicos: **um objetivo, uma prova**.

Regra: cada template deve citar a **fatia** do spec que está sendo materializada e o **teste** que prova o comportamento.

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
- Saídas canônicas (artefatos; formato; invariantes)
- Regras e encerramentos antecipados
- Limites e janelas (timeout, janela máxima, rate limit / retry)
- Idempotência/concorrência
- Observabilidade (logs/métricas e motivos de parada)

Checklist anti-alucinação (obrigatório fixar no contrato):
- timezone (qual e como é configurada) e como derivar “hoje”
- definição de “dia útil” (segunda–sexta? feriados? fonte do calendário)
- regra do “20h” (comparação exata)
- expressão CRON final (Azure Functions com segundos)
- primeira execução (state ausente) e resolução de inconsistências table vs blob
- invariantes do blob (ordem, dedupe, sobrescrita, serialização canônica)
- classificação de erros (falha dura vs parada segura) e tratamento de lacunas com erro/404
- precedência e parâmetros de rate limit / retry / pacing (Retry-After vs 1 req/min; timeouts; intervalo; limite)
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

