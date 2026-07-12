# Orientação para agentes de IA (portátil e agnóstico)

Índice dos ficheiros deste kit: `README.md`.

Este arquivo é um **mapa cognitivo mínimo** para agentes de IA trabalhando neste repositório. Ele não substitui os specs. Se uma mudança afetar **semântica**, **contrato** ou **métricas/indicadores**, atualize **docs + testes + código** juntos.

## Intenção do repositório (modelo)

Construir um sistema **descritivo** e **determinístico** que:

- calcula indicadores/artefatos do seu domínio de forma rastreável;
- expõe uma **superfície pública por contrato** (API/CLI/SDK/tools — o transporte é decisão posterior);
- suporta análises reproduzíveis (e geração reproduzível **apenas** quando um `seed` explícito for fornecido);
- produz outputs explicáveis: *quais regras foram aplicadas*, *qual janela/recorte*, *versões*, *metadados de rastreabilidade*.

> <PLACEHOLDER-DOMINIO>
> Substitua “indicadores/artefatos” por entidades do seu domínio (ex.: métricas, features, relatórios, transforms, regras, filtros, rankings).

## Anti-objetivos (modelo)

- promessas preditivas/garantias (“vai acontecer”, “vai melhorar chance”, etc.);
- defaults ocultos no servidor/sistema quando o pedido é ambíguo;
- “LLM dentro do servidor” interpretando texto livre no lugar do contrato.

## Fontes de verdade (ordem sugerida)

1. `docs/brief.md` — escopo, restrições, não-objetivos, política de determinismo; envelope e superfície em alto nível (placeholders)
2. `docs/adrs/0003-sync-bulk-results-all-only.md` — sync exclusivo via `/results/all` (breaking change)
3. `docs/contract-test-plan.md` — fixtures/goldens e ordem dos testes de contrato
3. `docs/spec-driven-execution-guide.md` — ordem prática: spec → teste → código
4. `docs/fases-execucao-templates.md` — templates atômicos copy/paste
5. `docs/test-plan.md` — definição de cobertura e matriz
6. `docs/project-guide.md` — estrutura (placeholder) + fronteiras
7. `docs/glossary.md` — linguagem humana (opcional)

## Guia operacional (execução local)

- Exemplo de `local.settings.json` (apenas **operacional**, não normativo): ver seção **“Execução local (exemplo de `local.settings.json`)”** em `README.md`.

> <PLACEHOLDER-NOMES-DE-ARQUIVO>
> Este kit não inclui ficheiros normativos de **contrato detalhado** nem de **catálogo de indicadores**; crie-os no seu repositório (com os nomes que preferir) e acrescente-os ao mapa em `AGENTS.md`.
> ADRs: use uma pasta `docs/adrs/` apenas quando fizer sentido (não faz parte do kit copiado).

## Não negociáveis

- **Spec-driven**: nada de “feature” sem citar o recorte do spec.
- **TDD / contrato primeiro**: testes precisam provar o recorte.
- **Determinismo**: mesma entrada canônica ⇒ mesma saída canônica (quando aplicável).
- **Sem defaults ocultos**: ambiguidades devem ser resolvidas no cliente/host (perguntas objetivas), não por inferência silenciosa do servidor/sistema.
- **Fatias pequenas**: um objetivo por change set.

