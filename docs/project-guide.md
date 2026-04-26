# Guia de projeto

## Objetivo

Ter fronteiras claras para que a **semântica** e o **contrato público** permaneçam estáveis conforme o sistema evolui — independentemente de stack, linguagem ou protocolo.

## Regras de fronteira (o que evita ambiguidade)

- O **núcleo de semântica** não depende de transporte (API/CLI) nem de IO concreto.
- A **superfície pública** não contém cálculo do domínio; ela valida/parsa e chama casos de uso.
- Integrações (IO/persistência/rede) não definem semântica; apenas fornecem dados e primitivas técnicas (ex.: hash, canonicalização).
- Se o contrato público mudar, atualizar **docs + testes + código** juntos.

## Recorte atual: Lotofacil-Loader (Azure Function)

Este repositório, no recorte atual, implementa um **loader determinístico** para **atualizar resultados da Lotofácil** e persistir:

- **um documento JSON** em **Blob Storage** (blob `Lotofacil`) para consumo externo via SAS (o consumo não é implementado aqui);
- **estado mínimo** em **Table Storage** (ex.: último concurso carregado) para retomar progresso e evitar trabalho redundante.

### Superfície pública (por contrato)

- **Entry point**: Azure Function com **Timer Trigger**.
- **Contrato observado**:
  - Regras de encerramento antecipado (dia útil, “após 20h” em timezone explícita, sorteio do dia já carregado).
  - Janela máxima interna de execução (**3 minutos**).
  - Dois modos de consulta à API: “último resultado” e “por concurso”.
  - Persistência em ordem: **blob primeiro**, **estado depois**.

> Nota: os detalhes completos de contrato e mapeamento estão na seção **Contrato V0 — Lotofacil Loader (normativo)** em `docs/spec-driven-execution-guide.md` e nas decisões em `docs/adrs/*`.

### Estrutura sugerida (C#/.NET em um único Function App)

O objetivo é manter o trigger fino (orquestração) e isolar semântica e IO:

```text
src/
  FunctionApp/         # TimerTrigger: binding, logging, wiring; sem lógica de domínio
  Application/         # casos de uso (ex.: UpdateLotofacilResults) e políticas (Polly)
  Domain/              # modelos canônicos: LotofacilDraw, documento do blob, invariantes
  Infrastructure/      # IO concreto: HTTP API client, BlobClient, TableClient, serialização
  Composition/         # DI: ServiceCollection, HttpClientFactory, options/config
tests/
  contract/            # testes do contrato de IO/shape (goldens/fixtures)
  application/         # testes de caso de uso (com portas fakes)
  domain/              # invariantes/modelos
docs/
  brief.md
  project-guide.md
  glossary.md
  adrs/
```

### Portas (interfaces) e adaptadores (implementações)

- **Portas (Application → exterior)**:
  - `ILotofacilApiClient`: consulta `/results/last` e `/results/{id}`.
  - `ILotofacilBlobStore`: lê/grava documento do blob.
  - `ILotofacilStateStore`: lê/escreve estado do último concurso (Table Storage).
  - `IClock` (opcional): abstração de tempo para eliminar defaults e facilitar testes (timezone explícita).
- **Adaptadores (Infrastructure)**:
  - Cliente HTTP com `HttpClientFactory` + Polly (retry/timeout) e suporte a 429/`Retry-After`.
  - Persistência Blob: gravação coerente (documento completo) com `Content-Type` JSON.
  - Persistência Table: uso de ETag para concorrência otimista e atualização atômica do estado.

### Configuração (sem segredos em código)

- Token da API e credenciais/Access Key do Storage devem vir de **variáveis de ambiente**.
- Nomes sugeridos (padrão `.NET` `Section__Key`) estão em `docs/brief.md`.

### Decisões que ficam fora do guia (via ADR)

Quando a decisão tiver trade-offs e impacto operacional, registrar em ADR (ex.: timezone padrão do ambiente, estratégia de identidade para acesso ao Storage, nome do container do blob, política exata de rate limit/pacing).

