# Guia de execução spec-driven (portátil e agnóstico)

## Definição

“Spec-driven” aqui significa:

1. a fonte de verdade vem primeiro em `docs/`
2. cada implementação é uma **fatia explícita** (um recorte pequeno)
3. cada fatia tem testes que provam que ela está correta
4. código sem referência clara ao spec é suspeito
5. mudanças semânticas exigem atualização coordenada de **docs + testes + código**

## Regra operacional

Antes de pedir a um agente para implementar algo, responda:

- Qual fatia do spec eu estou materializando?
- Qual é o conjunto mínimo de arquivos que isso deve tocar?
- Qual teste prova que está correto?
- Cabe em um change set revisável?

Se alguma resposta estiver pouco clara, a fatia ainda não está pronta.

## Fontes de verdade (ordem prática)

- `docs/adrs/*`: decisões arquiteturais (ex.: Azure Function + Storage)
- este documento (`docs/spec-driven-execution-guide.md`): contrato de execução **normativo** da V0 + ordem de execução spec-driven
- `docs/fases-execucao-templates.md`: templates atômicos de implementação por fatias

> Regra: **não** existe implementação V0 sem a seção **“Contrato V0 — Lotofacil Loader (normativo)”** definida neste documento.

## Contrato mínimo que toda fatia deve explicitar

Para cada fatia, documente explicitamente (sem inferência):

- **Entradas canônicas**: quais configs/variáveis de ambiente existem, quais são obrigatórias, e seus formatos
- **Saídas canônicas**: quais artefatos são produzidos (ex.: blob JSON), formato e invariantes
- **Regras e encerramentos antecipados**: quando a execução termina sem efeitos (e por quê)
- **Limites e janelas**: timeouts, janela máxima de trabalho, rate-limit / retry
- **Idempotência/concorrência**: como reexecuções e instâncias múltiplas não corrompem estado
- **Observabilidade**: quais métricas/logs evidenciam comportamento e por qual motivo parou

## Anti-alucinação: decisões que o contrato da V0 DEVE fixar

O objetivo desta seção é impedir que a implementação “complete lacunas” por inferência. Se algum item abaixo ficar em aberto, o contrato não está pronto.

- **Timezone (obrigatório)**:
  - qual timezone usar (identificador) e como ela é configurada/fornecida em runtime
  - como derivar “hoje” (data) nessa timezone
- **Definição de “dia útil” (obrigatório)**:
  - é somente “segunda–sexta” ou inclui feriados? se inclui feriados, qual fonte/calendário e como é fornecido
- **Regra do “20h” (obrigatório)**:
  - o que significa “passou das 20h” (>= 20:00:00?) e como tratar segundos/minutos
- **Timer/CRON (obrigatório)**:
  - expressão CRON final (formato Azure Functions com segundos) e expectativa de execução
- **Primeira execução (state ausente) (obrigatório)**:
  - qual `LastLoadedContestId` inicial
  - como proceder se o blob já existir (prioridade: table vs blob) e como resolver inconsistências
- **Formato e invariantes do blob (obrigatório)**:
  - ordenação de `draws` (por `contest_id` crescente?) e se deve haver deduplicação
  - comportamento quando um `contest_id` já existe no blob (sobrescreve? ignora? falha?)
  - escrita coerente: qual estratégia para evitar leitores verem conteúdo parcial (ex.: “escreve tudo e substitui”)
  - serialização canônica (por exemplo: propriedades, casing, ordenação de campos, e normalização de datas) para estabilidade de golden
- **Erros e classificação (obrigatório)**:
  - quais erros encerram a execução como falha (ex.: config inválida) vs. encerramento seguro (ex.: janela expirou)
  - política quando a API não retorna um concurso específico (404/erro) no meio da lacuna
- **Rate limit / retry / pacing (obrigatório)**:
  - precedência entre `Retry-After` e a regra “1 req/min”
  - valores finais (ou ranges) para timeout por request, intervalo de retry e limite máximo dentro da janela de 3 minutos
- **Concorrência (obrigatório)**:
  - como evitar duas instâncias corromperem blob/state (ETag no Table é necessário, mas o contrato deve dizer o que ocorre no conflito)
  - comportamento esperado quando houver conflito (ex.: “aborta e deixa para o próximo tick”)
- **Ordem de persistência e checkpoint (obrigatório)**:
  - confirmar que, ao processar múltiplos ids, o checkpoint (`LastLoadedContestId`) sempre representa o último concurso efetivamente gravado no blob

## Contrato V0 — Lotofacil Loader (normativo)

Esta seção é **a especificação final** para a IA implementar a V0. Nada aqui deve depender de documentos “de contexto” arquivados.

### 1) Stack e superfície pública

- **Runtime**: Azure Functions em **C#/.NET**
- **Trigger**: **Timer Trigger**
- **Semântica**: o trigger apenas orquestra; a lógica deve ser testável sem Azure (ports/adapters).

### 2) Regras de calendário (fechadas para evitar inferência)

- **Timezone de referência**: `America/Sao_Paulo` (IANA).
- **Definição de “dia útil”**: **segunda a sexta**, sem considerar feriados (V0).
- **Regra do “20h”**: “passou das 20h” significa **hora local >= 20:00:00** na timezone de referência.

### 3) Agendamento (CRON)

- **CRON normativo (Azure Functions, com segundos)**: `0 0 * * * *` (a cada hora, minuto 0).

### 4) Janela máxima e política de execução

- **Janela máxima por execução**: 3 minutos (180s) de trabalho interno.
- Ao atingir a janela, a execução **encerra de forma segura** e o processamento **retoma** na próxima execução do timer.

### 5) API de fonte (Lotodicas)

- **Base URL**: `https://www.lotodicas.com.br` (configurável).
- **Autenticação**: token via query string `token=<TOKEN>` (segredo via ambiente).
- **Endpoints normativos**:
  - **Último concurso**: `/api/v2/lotofacil/results/last?token=<TOKEN>`
  - **Concurso por id**: `/api/v2/lotofacil/results/{id}?token=<TOKEN>`
- **Campos mínimos consumidos do JSON**:
  - `data.draw_number` (id do concurso)
  - `data.draw_date` (data do sorteio, formato `YYYY-MM-DD`)
  - `data.drawing.draw` (lista de números)
  - `data.prizes[]` com item `name == "15 acertos"` e campo `winners`

### 6) Artefato no Blob (contrato de saída)

- **Nome do blob**: `Lotofacil`
- **Content-Type**: `application/json; charset=utf-8`
- **Formato do documento**:

```json
{
  "draws": [
    {
      "contest_id": 1,
      "draw_date": "2003-09-29",
      "numbers": [2, 3, 5, 6, 9, 10, 11, 13, 14, 16, 18, 20, 23, 24, 25],
      "winners_15": 5,
      "has_winner_15": true
    }
  ]
}
```

- **Mapeamento API → blob**:
  - `contest_id` ← `data.draw_number`
  - `draw_date` ← `data.draw_date`
  - `numbers` ← `data.drawing.draw`
  - `winners_15` ← `data.prizes[]` onde `name == "15 acertos"`, campo `winners`
  - `has_winner_15` ← `winners_15 > 0`
- **Invariantes do blob (V0)**:
  - `draws` deve estar **ordenado por `contest_id` ascendente**
  - `contest_id` deve ser **único** (dedupe por `contest_id`)
  - se um `contest_id` já existir, o item deve ser **sobrescrito** pelo novo cálculo (idempotência)
  - a escrita deve ser **coerente**: persistir o documento completo (sem “append” parcial). Em caso de reexecução, o documento final deve continuar válido.
  - não incluir metadados extras (ex.: `schema_version`, timestamps) na V0

### 7) Estado no Table Storage (contrato de checkpoint)

- **Papel**: armazenar/consultar o “último concurso carregado” para evitar redundância e retomar lacunas.
- **Tabela (V0)**: `LotofacilState`
- **Chaves (V0)**:
  - `PartitionKey = "Lotofacil"`
  - `RowKey = "Loader"`
- **Campos mínimos (V0)**:
  - `LastLoadedContestId` (inteiro)
  - `LastLoadedDrawDate` (string `YYYY-MM-DD` ou tipo data, mas o valor lógico deve ser a data do último concurso carregado)
  - `LastUpdatedAtUtc` (timestamp UTC)
- **Concorrência**: usar **ETag** (concorrência otimista). Se falhar por conflito, encerrar execução de forma segura e deixar para o próximo tick.

### 8) Primeira execução e inconsistências (V0)

- Se o state no Table **não existir**:
  - ler o blob `Lotofacil` se existir:
    - se existir e contiver `draws`, derivar `LastLoadedContestId = max(draws[].contest_id)` e `LastLoadedDrawDate` do item correspondente, e persistir state
    - se não existir (ou `draws` vazio), inicializar `LastLoadedContestId = 0` e `LastLoadedDrawDate = null`

### 9) Algoritmo de atualização (normativo, com encerramentos)

1. Ler state no Table (`LastLoadedContestId`, `LastLoadedDrawDate`).
2. **Encerramento antecipado (antes de chamar API)**:
   - se hoje **não** é dia útil: encerrar
   - se hoje é dia útil e hora local < 20:00:00: encerrar
   - se hoje é dia útil, hora local >= 20:00:00, e `LastLoadedDrawDate == hoje`: encerrar
3. Chamar endpoint “último” e obter `latestId = data.draw_number`.
4. Se `latestId <= LastLoadedContestId`: encerrar (alinhado; sem downloads por id; sem persistências).
5. Calcular lacunas `id` de `LastLoadedContestId + 1` até `latestId`, em ordem crescente.
6. Processar ids em ordem crescente, respeitando a janela de 3 minutos:
   - para cada `id`, chamar `/results/{id}` com resiliência (ver seção 10)
   - atualizar o documento do blob em memória incluindo/sobrescrevendo o `id` processado
   - ao final (ou ao expirar janela), persistir conforme seção 11
7. Se a janela expirar, encerrar e retomar no próximo tick a partir do state persistido.

### 10) Resiliência, rate limit e pacing (normativo)

- **Retries**: usar Polly para falhas transitórias (timeouts, 5xx, 429).
- **429 / Retry-After**:
  - se houver `Retry-After`, ele tem **precedência** sobre qualquer intervalo fixo, desde que não ultrapasse a janela restante
- **Intervalo de retry quando não houver Retry-After**: 30s
- **Teto**: nenhuma tentativa pode extrapolar a janela de 3 minutos; se extrapolar, encerrar e retomar no próximo tick.
- **Pacing 1 req/min**:
  - garantir ~60s entre chamadas quando aplicável; se isso impedir concluir todos os ids na janela, encerrar e retomar.

### 11) Persistência (ordem e garantias)

- **Ordem obrigatória**: gravar **blob primeiro**, atualizar **Table depois**.
- **Regra de checkpoint**: `LastLoadedContestId` só pode avançar até o **último `contest_id` que está refletido no blob persistido**.
- Se falhar ao gravar o blob: **não atualizar** o Table.
 
### 12) Configuração por variáveis de ambiente (V0)

- `Lotodicas__BaseUrl` (obrigatória)
- `Lotodicas__Token` (obrigatória; segredo)
- `Storage__ConnectionString` (obrigatória; segredo) *(V0 usa connection string/access key via ambiente)*
- `Storage__BlobContainer` (obrigatória)
- `Storage__LotofacilBlobName` (obrigatória; deve ser `Lotofacil`)
- `Storage__LotofacilStateTable` (obrigatória; deve ser `LotofacilState`)

## V0 do sistema (fatia alvo deste repositório)

Com base nas decisões em `docs/adrs/*` e no **Contrato V0 (normativo)** acima, a V0 materializa:

- **Timer Trigger** em Azure Functions (C#/.NET)
- **Fonte**: API (modo “último resultado” e modo “por id”)
- **Estado**: Table Storage com “último concurso carregado” (+ ETag)
- **Artefato público**: Blob JSON (nome do blob `Lotofacil`) contendo `draws[]`
- **Restrições**: janela de execução de ~3 minutos; retry/pacing para não violar limite do provedor; sem segredos em código
- **Encerramentos antecipados**: dia não útil / antes das 20h (timezone explícita) / já carregou sorteio do dia / latestId <= lastLoaded

## Ordem recomendada de trabalho (passo a passo, completo)

### 1) Congelar o envelope do contrato em `docs/`

- **Definir o contrato de execução V0** (um documento único e normativo em `docs/`):
  - **Config**: nomes, obrigatoriedade e validação das variáveis (ex.: `Lotodicas__BaseUrl`, `Lotodicas__Token`, storage, tabela, contentor, blob name)
  - **Timezone**: declarar como será configurada (sem assumir “local”)
  - **Cron**: declarar expressão e expectativa (ex.: “a cada hora, minuto 0”)
  - **Janela**: “até 3 minutos” e o que acontece quando expira (retoma no próximo tick)
  - **Rate limit**: regras (429/Retry-After, 1 req/min quando aplicável)
  - **Ordem de persistência**: blob primeiro, table depois
  - **Modelo do blob**: JSON `{ draws: [...] }` e mapeamento API → draw
  - **Modelo do state** (table): PK/RK e campos mínimos (`LastLoadedContestId`, `LastLoadedDrawDate`, `LastUpdatedAtUtc`, ETag)
  - **Erros**: o que é falha dura vs. “parada segura” (encerramento antecipado)

### 2) Definir fixtures e goldens (determinísticos)

- **Fixtures de API**:
  - resposta do endpoint “último” (`/results/last`)
  - respostas por concurso (`/results/{id}`) incluindo casos: 200, 429 com Retry-After, 5xx, timeout
- **Fixtures de calendário**:
  - dia útil antes das 20h
  - dia útil após as 20h
  - fim de semana (ou “não útil”)
- **Fixtures de storage**:
  - table state inexistente (primeira execução)
  - state existente com `LastLoadedContestId = N`
  - conflito de ETag
- **Golden do blob**:
  - exemplo mínimo do documento `{ draws: [...] }` esperado para uma sequência pequena de concursos

### 3) Escrever testes de contrato (vermelhos primeiro)

- **Regras de encerramento antecipado**:
  - não útil ⇒ não chama API, não escreve blob/table
  - antes das 20h ⇒ idem
  - `LastLoadedDrawDate == hoje` após 20h ⇒ idem
- **Alinhamento com o “último”**:
  - `latestId <= lastLoaded` ⇒ não baixa por id, não escreve
- **Preenchimento de lacunas**:
  - baixa ids de `lastLoaded+1..latestId` em ordem
  - respeita janela (para no meio e persiste apenas o que concluiu)
- **Resiliência**:
  - 429 respeita Retry-After (quando presente) e não excede cadência
  - falhas transitórias aplicam retry com limite que caiba na janela
- **Persistência e atomicidade lógica**:
  - blob é escrito antes de atualizar table state
  - em falha ao gravar blob ⇒ não atualiza table
  - em conflito de ETag ⇒ comportamento seguro (não corromper; próxima execução retoma)

### 4) Criar esqueleto mínimo que compila e roda testes

- **Projeto**: Azure Functions isolada ou in-process conforme ADR (se existir); manter trigger fino (orquestração)
- **Camadas** (sugestão arquitetural): `FunctionApp/`, `Application/`, `Domain/`, `Infrastructure/`, `Composition/`
- **DI**: `HttpClientFactory`; portas (interfaces) no núcleo; adaptadores na infraestrutura

### 5) Implementar núcleo semântico mínimo (até passar testes)

- **Modelos de domínio**:
  - `LotofacilDraw` (campos do blob)
  - `LotofacilDrawDocument` (`draws`)
  - DTOs da API (ou parsing robusto)
- **Caso de uso**: “UpdateLotofacilResults”
  - calcula encerramento antecipado
  - consulta state, chama “last”, calcula gaps
  - processa ids com controle de tempo (deadline) + pacing + retry
  - aplica ordem de persistência (blob → table)

### 6) Implementar infraestrutura (adaptadores)

- **Cliente API**:
  - baseUrl + token via config (nunca hardcoded)
  - políticas Polly (retry, timeout) e respeito a 429/Retry-After
  - pacing para 1 req/min quando aplicável (sem estourar a janela)
- **Blob store**:
  - lê/escreve documento JSON completo com Content-Type adequado
  - escrita coerente (não expor parcial)
- **Table store**:
  - lê state, escreve com concorrência otimista (ETag)

### 7) Fechar evidência da V0 (reprodutível)

- Rodar suíte de testes e registrar (em `docs/test-plan.md` ou arquivo de evidência) o que foi validado
- Garantir que a V0 não introduz “defaults ocultos” (timezone/config/rate limit explicitados)
- Verificar que a execução é **idempotente** e que a próxima execução retoma após janela expirar

## Checklist de revisão (antes de aceitar uma mudança)

- **Docs**: a mudança cita a fatia do spec e atualiza contrato/ADR quando mexe em semântica
- **Testes**: existe teste que prova a regra/novo comportamento
- **Determinismo**: mesma entrada canônica ⇒ mesma saída canônica (quando aplicável)
- **Segredos**: nenhum token/chave em código; tudo por ambiente
- **Operação**: logs explicam “por que parou” (encerramento antecipado, janela, rate limit, erro)

> Nota: este guia não impõe linguagem/framework. Ajuste os nomes de camadas/pastas conforme `docs/project-guide.md`.

