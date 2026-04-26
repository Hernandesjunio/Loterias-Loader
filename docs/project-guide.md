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

### Organização de pastas e arquivos (convenções do repositório)

Estas convenções existem para melhorar **descoberta**, **revisão** e **evolução por fatias** (spec-driven), mantendo as camadas e contratos fáceis de navegar.

- **Preferência: 1 tipo público por arquivo**:
  - Classes / interfaces / enums / records **públicos** devem, por padrão, viver em arquivos separados.
  - O nome do arquivo deve bater com o nome do tipo (ex.: `ILotofacilApiClient.cs`, `LotofacilDraw.cs`).
- **Pastas por camada e responsabilidade** (dentro de cada projeto/camada):
  - Ex.: `Ports/`, `UseCases/`, `Models/`, `Options/`, `Http/`, `Storage/`.
  - Evitar “pasta genérica” (`Misc`, `Common`) sem critério; quando necessário, explicitar o motivo.
- **Exceções aceitáveis (explícitas)**:
  - Tipos **privados** e estritamente locais (helpers internos) podem ficar no mesmo arquivo do tipo principal.
  - Tipos muito pequenos e coesos podem ser agrupados quando isso melhora legibilidade (ex.: `Options` relacionadas), desde que o arquivo não vire “depósito”.
  - Em testes, agrupar fakes/fixtures no mesmo arquivo pode ser aceitável quando isso reduz fricção e mantém a fatia pequena.

> Regra prática: se um arquivo começar a concentrar “vários assuntos” (porta + modelo + caso de uso, etc.), ele deve ser dividido.

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

### Estratégia de testes (sem ambiguidade de fronteira)

Este repositório privilegia testes determinísticos e reprodutíveis.

- **Integração com terceiros (Lotodicas)**:
  - Em testes de integração automatizados, tratar como **Fake** (test double) para evitar não-determinismo de rede/serviço.
  - Quando necessário, complementar com testes de contrato separados para garantir fidelidade do Fake ao contrato observado.
- **Integração com Storage**:
  - Preferir execução local controlada (ex.: emulador) para Blob+Table.
  - Quando a qualidade exigir, aceitar execução com dependências em container (para isolar/limpar ambiente com consistência).

> Nota: detalhes operacionais e cobertura vivem em `docs/test-plan.md`; este guia apenas fixa a fronteira e a intenção.

### Decisões que ficam fora do guia (via ADR)

Quando a decisão tiver trade-offs e impacto operacional, registrar em ADR (ex.: timezone padrão do ambiente, estratégia de identidade para acesso ao Storage, nome do container do blob, política exata de rate limit/pacing).

