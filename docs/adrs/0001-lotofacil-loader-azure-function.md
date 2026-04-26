---
status: rascunho
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

### 1) Timezone de referência para “dia útil” e “20h”

- **Problema**: evitar defaults ocultos; a função precisa saber qual timezone aplicar ao avaliar “hoje” e “após 20h”.
- **Opções**:
  - (A) Configurar por variável de ambiente (ex.: `Lotofacil__TimeZoneId`) e tratar como obrigatória.
  - (B) Fixar explicitamente no código (ex.: `America/Sao_Paulo`) — simples, porém vira “default” embutido.
- **Pergunta para validação**: qual timezone deve ser considerada a fonte de verdade?

### 2) Acesso ao Storage Account (Blob/Table)

- **Problema**: na conversa foi citado uso de **Access Key/connection string** via ambiente; há alternativas mais “managed”.
- **Opções**:
  - (A) Connection string / Access Key via variáveis de ambiente (alinhado ao contexto atual).
  - (B) Managed Identity + RBAC (reduz segredo, muda setup do Azure).
- **Pergunta para validação**: manter (A) como primeira entrega ou já ir para (B)?

### 3) Nome do container do blob

- **Problema**: foi definido apenas o **nome do blob** (`Lotofacil`), não o nome do **container**.
- **Opções**:
  - (A) Definir por variável de ambiente (`Storage__BlobContainer`).
  - (B) Fixar um container padrão (vira default embutido).
- **Pergunta para validação**: qual nome de container (e política de criação) será usada?

### 4) Política de rate limit / pacing e janela de 3 minutos

- **Problema**: existe restrição “1 pedido/minuto” mencionada e uma janela interna de 3 minutos; isso limita quantos concursos podem ser preenchidos por execução.
- **Opções**:
  - (A) Respeitar `Retry-After` e aplicar pacing mínimo (ex.: 30s) quando aplicável; processar no máximo 1–6 concursos por execução dependendo do tempo restante.
  - (B) Implementar “modo bootstrap” com execução manual/temporária para preencher histórico (fora do timer), mantendo o timer para manutenção.
- **Pergunta para validação**: precisamos de um caminho explícito para bootstrap histórico?

### 5) Modelo e formato do documento no blob

- **Problema**: a conversa definiu `draws[]` e os campos, mas não definiu ordenação, versionamento e metadados.
- **Opções**:
  - (A) `draws` ordenado por `contest_id` ascendente, sem metadados (mínimo).
  - (B) Incluir metadados de rastreabilidade (ex.: `generated_at_utc`, `source`, `schema_version`).
- **Pergunta para validação**: o blob deve carregar metadados e versão de schema já na primeira versão?

### 6) Concorrência: como evitar duas execuções simultâneas

- **Problema**: timer pode sobrepor execuções; a conversa menciona ETag no Table como mitigação.
- **Opções**:
  - (A) Confiar em ETag no Table + idempotência (detectar e abortar ao falhar update por ETag).
  - (B) Lock explícito (blob lease / entidade lock no Table).
- **Pergunta para validação**: ETag é suficiente para o seu cenário?

## Referências

- `docs/spec-driven-execution-guide.md`
- `docs/fases-execucao-templates.md`

