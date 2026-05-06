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
- **Feature toggles operacionais**: quais verificações/regras podem ser habilitadas/desabilitadas por configuração, com defaults explícitos
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

Esta seção é **a especificação final e testável** para implementar a V0. Ela deve permitir escrever **testes de contrato** sem “inventar” comportamento. Quando houver conflito, prevalece este contrato e as decisões em `docs/adrs/*` (ver `docs/adrs/0001-lotofacil-loader-azure-function.md`).

### 1) Stack e superfície pública (V0)

- **Runtime**: Azure Functions em **C#/.NET**.
- **Trigger**: **Timer Trigger**.
- **Arquitetura**: ports & adapters (ver ADR 0001). O trigger é fino (orquestração + observabilidade) e a lógica é testável sem Azure.

### 2) Entradas canônicas (config/ambiente) — obrigatórias e validações (V0)

Todas as entradas abaixo são **variáveis de ambiente** (config), lidas no início. Se alguma validação falhar, a execução é **falha dura** e termina **sem efeitos** (não chama API; não escreve blob/table).

- **`Lotodicas__BaseUrl`** (obrigatória)
  - **Formato**: URL absoluta HTTPS.
  - **Normalização canônica**: sem barra final (ex.: `https://www.lotodicas.com.br`).
- **`Lotodicas__Token`** (obrigatória; segredo)
  - **Formato**: string não vazia (após trim).
- **`Storage__ConnectionString`** (obrigatória; segredo)
  - **Formato**: connection string válida para Azure Storage (V0 usa key/connection string via ambiente; ver ADR 0001).
- **`Storage__BlobContainer`** (obrigatória)
  - **Formato**: nome de container válido do Blob Storage (minúsculas, números e hífen; deve existir ou ser criável pela infraestrutura).
- **`Storage__LotofacilBlobName`** (obrigatória)
  - **Valor normativo (V0)**: **`Lotofacil`** (case-sensitive).
- **`Storage__LotofacilStateTable`** (obrigatória)
  - **Valor normativo (V0)**: **`LotofacilState`**.
- **Agendamento do Timer Trigger (V0.1+; adendo incremental)**:
  - ver seção **4.1** (“Agendamento configurável por configuração do host”), que introduz `LotofacilLoader__TimerSchedule` como entrada canônica obrigatória para V0.1+.
- **Feature toggles — travas de calendário (V0.x; operacionais, opcionais)**:
  - **`LotofacilLoader__DisableBusinessDayGuard`** (opcional)
    - **Formato**: boolean (`true`/`false`, case-insensitive).
    - **Default normativo**: `false`.
    - **Efeito**: quando `true`, **desabilita** o encerramento antecipado “hoje não é dia útil” (permite execução em finais de semana).
  - **`LotofacilLoader__Disable20hGuard`** (opcional)
    - **Formato**: boolean (`true`/`false`, case-insensitive).
    - **Default normativo**: `false`.
    - **Efeito**: quando `true`, **desabilita** o encerramento antecipado “antes das 20h” (permite execução antes do horário de corte).
- **Timezone/relógio (normativo, não configurável na V0)**:
  - **Timezone de referência**: `America/Sao_Paulo` (IANA).
  - **Como derivar “agora” e “hoje”**: obter `nowLocal` convertendo um relógio de referência (UTC) para `America/Sao_Paulo`; então `todayLocal = date(nowLocal)` (apenas a data nessa timezone).

### 3) Regras de calendário (fechadas para evitar inferência) (V0)

- **Definição de “dia útil”**: **segunda a sexta**, **sem considerar feriados** (V0). Fonte: somente regra ISO weekday; não há calendário externo.
- **Regra do “20h”**: “passou das 20h” significa **`nowLocal >= todayLocal 20:00:00`** na timezone de referência, com comparação exata (inclui segundos).

### 4) Agendamento (CRON) (V0)

- **CRON normativo (Azure Functions Timer Trigger, com segundos)**: **`0 0 * * * *`**.
  - Interpretação: a cada hora, no minuto 0, segundo 0.

### 4.1) Adendo incremental — Agendamento configurável por configuração do host (V0.1)

Este adendo existe para remover rigidez operacional (CRON hardcoded) e permitir ajustar o schedule por ambiente **sem redeploy**, mantendo o contrato explícito (sem defaults ocultos).

- **Nova entrada canônica obrigatória (V0.1)**:
  - **`LotofacilLoader__TimerSchedule`** (obrigatória)
    - **Formato**: expressão NCRONTAB do Timer Trigger do Azure Functions (6 campos, com segundos).
    - **Valor recomendado** (equivalente ao V0): `0 0 * * * *`.
- **Resolução do schedule (normativo, V0.1)**:
  - O Timer Trigger deve referenciar o schedule por *name resolver* usando a sintaxe `%...%` (ex.: `"%LotofacilLoader__TimerSchedule%"`).
  - O valor deve vir de **Application Settings / variáveis de ambiente** do Function App (e `local.settings.json` em desenvolvimento local).
- **Compatibilidade**:
  - Este adendo **supersede** a regra “CRON fixo em código” do item 4 para execuções V0.1+.
  - O valor do CRON continua sendo parte do contrato (só mudou a *fonte* do valor: config ao invés de código).

Referência técnica (normativa do runtime):
- Microsoft Learn: Timer trigger suporta schedule por app setting usando `%ScheduleAppSetting%`.

### 5) Limites, janelas e timeouts (V0)

- **Janela máxima por execução**: **180s** contados do início da função.
  - Ao atingir o deadline (ou não haver tempo suficiente para progredir com segurança), a execução **para com parada segura** e **retoma** no próximo tick.
- **Timeout por request HTTP** (cada tentativa): **10s**.
- **Orçamento mínimo para iniciar uma nova tentativa**: se o tempo restante até o deadline for menor que **15s**, encerrar com parada segura (para evitar iniciar IO sem chance de concluir e persistir).

### 6) API de fonte (Lotodicas) — endpoints e parsing mínimo (V0)

- **Base URL canônica**: `Lotodicas__BaseUrl`.
- **Autenticação**: token via query string `token=<TOKEN>` (segredo em `Lotodicas__Token`).
- **Endpoints normativos**:
  - **Último concurso**: `/api/v2/lotofacil/results/last?token=<TOKEN>`
  - **Concurso por id**: `/api/v2/lotofacil/results/{id}?token=<TOKEN>`
  - **Carga inicial (bulk)**: `/api/v2/{lotteryApiSegment}/results/all?token=<TOKEN>` (sem paginação; retorna todos os concursos desde o primeiro)
- **Campos mínimos consumidos do JSON** (qualquer ausência/tipo inesperado é **falha dura**):
  - `data.draw_number` (inteiro; id do concurso)
  - `data.draw_date` (string; `YYYY-MM-DD`)
  - `data.drawing.draw` (array de inteiros)
  - `data.prizes[]` contendo item com `name == "15 acertos"` e campo `winners` (inteiro; pode ser 0)
  - Para `/results/all`: `data[]` é um array de objetos com os mesmos campos acima por item.

### 7) Saídas canônicas — artefato no Blob (V0)

- **Artefato**: 1 documento JSON no Blob Storage.
- **Container**: `Storage__BlobContainer`.
- **Nome do blob**: `Storage__LotofacilBlobName` (normativo: `Lotofacil`).
- **Content-Type**: `application/json; charset=utf-8`.
- **Formato do documento (canônico)**:

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

- **Mapeamento API → blob (canônico)**:
  - `contest_id` ← `data.draw_number`
  - `draw_date` ← `data.draw_date`
  - `numbers` ← `data.drawing.draw`
  - `winners_15` ← `data.prizes[]` onde `name == "15 acertos"`, campo `winners`
  - `has_winner_15` ← `winners_15 > 0`
- **Invariantes do blob (V0)**:
  - **Ordenação**: `draws` em **ordem crescente** por `contest_id`.
  - **Deduplicação**: `contest_id` é **único**; ao (re)processar um `contest_id`, o item final no blob deve refletir o último cálculo determinístico (sobrescrita lógica).
  - **Serialização canônica (para golden tests)**:
    - nomes de campos exatamente como no exemplo (`snake_case`);
    - `draw_date` sempre como string `YYYY-MM-DD`;
    - `numbers` como array de inteiros na ordem retornada pela API (não reordenar);
    - JSON sem campos extras na V0 (não adicionar `schema_version`, timestamps, etc.).
  - **Escrita coerente**: o blob é escrito como um documento completo e válido (sem “append” parcial). Leitores nunca devem observar uma representação inválida/partial.
  - **Sobrescrita física**: a persistência deve substituir o conteúdo do blob pelo documento completo atualizado.

### 8) Estado canônico — checkpoint no Table Storage (V0)

- **Papel**: armazenar/consultar o checkpoint para retomar lacunas e evitar redundância.
- **Tabela (V0)**: `Storage__LotofacilStateTable` (normativo: `LotofacilState`).
- **Chaves (V0)**:
  - `PartitionKey = "Lotofacil"`
  - `RowKey = "Loader"`
- **Campos mínimos (V0)**:
  - `LastLoadedContestId` (inteiro \(>= 0\))
  - `LastLoadedDrawDate` (string `YYYY-MM-DD` ou null)
  - `LastUpdatedAtUtc` (timestamp UTC)
- **Concorrência (Table)**: **ETag obrigatório** (concorrência otimista).
  - Em conflito de ETag ao atualizar o state: **parada segura** (não tentar “resolver” por inferência); próxima execução retoma.

### 9) Primeira execução e resolução de inconsistências (Table vs Blob) (V0)

Quando o state no Table **não existir**:

- Ler o blob `Lotofacil` (se existir).
  - Se existir e `draws` não estiver vazio:
    - derivar `LastLoadedContestId = max(draws[].contest_id)` e `LastLoadedDrawDate` do item correspondente;
    - persistir o state no Table com esses valores (observando ETag na escrita subsequente).
  - Se não existir (ou `draws` vazio):
    - executar **carga inicial (bulk)** via `/api/v2/{lotteryApiSegment}/results/all?token=<TOKEN>` e materializar o documento canônico do blob;
    - persistir **blob primeiro** (documento completo);
    - persistir o state no Table derivando:
      - `LastLoadedContestId = max(draws[].contest_id)` (ou `0` se a resposta estiver vazia);
      - `LastLoadedDrawDate` do item correspondente (ou `null` se vazio).

Se existir inconsistência entre Table e Blob:

- **Fonte de verdade do checkpoint é o blob persistido**.
  - Se `Table.LastLoadedContestId > max(blob.draws[].contest_id)`, tratar como **falha dura** (inconsistência de estado) e encerrar sem efeitos (não avançar; não reescrever blob “para casar”).

### 10) Regras e encerramentos antecipados (V0)

Encerramentos antecipados são **paradas seguras** (exit code “sucesso” operacional), e devem registrar **motivo de parada** (ver Observabilidade).

Antes de chamar a API “último”:

- Se `todayLocal` **não** é dia útil: encerrar, **a menos que** `LotofacilLoader__DisableBusinessDayGuard == true`.
- Se é dia útil e `nowLocal < todayLocal 20:00:00`: encerrar, **a menos que** `LotofacilLoader__Disable20hGuard == true`.
- Se é dia útil e `nowLocal >= todayLocal 20:00:00` e `LastLoadedDrawDate == todayLocal (YYYY-MM-DD)`: encerrar.

Após obter `latestId` pelo endpoint “último”:

- Se `latestId <= LastLoadedContestId`: encerrar (alinhado; não baixar por id; sem persistências).

### 11) Algoritmo normativo de atualização (V0)

1. Ler state no Table (`LastLoadedContestId`, `LastLoadedDrawDate`, ETag).
2. Aplicar encerramentos antecipados (seção 10).
2.1. Ler blob atual. Se o blob **não existir** ou existir com `draws` **vazio**:
   - executar a **carga inicial (bulk)** via `/api/v2/{lotteryApiSegment}/results/all?token=<TOKEN>`;
   - persistir **blob primeiro** e então atualizar o state no Table para o `max(contest_id)` materializado;
   - encerrar com sucesso (os endpoints incrementais servem para atualizar concursos novos após o bootstrap).
3. Chamar endpoint “último” e obter `latestId = data.draw_number`.
4. Se `latestId <= LastLoadedContestId`: encerrar.
5. Calcular lacunas `id` no intervalo **contíguo** \([LastLoadedContestId + 1, latestId]\), em ordem crescente.
6. Preparar documento alvo do blob em memória:
   - ler blob atual (se existir); se não existir, iniciar `{ "draws": [] }`.
   - normalizar/validar invariantes canônicas (seção 7) em memória antes de persistir.
7. Processar `id` em ordem crescente até expirar a janela:
   - chamar `/results/{id}` aplicando resiliência/rate-limit (seção 12);
   - em sucesso 200, mapear para o item canônico e **upsert** no documento em memória;
   - após cada id bem-sucedido, atualizar um marcador interno `lastPersistableId = id` (sempre contíguo).
8. Persistir (seção 13) o blob e então o state avançando **somente até `lastPersistableId`**.
9. Se a janela expirar em qualquer ponto: encerrar com parada segura; próxima execução retoma do checkpoint persistido.

### 12) Rate limit / retry / pacing — precedência e teto (V0)

- **Classificação base**:
  - **429**: respeitar `Retry-After` quando presente.
  - **5xx**, timeouts e falhas de rede: transitórios (passíveis de retry dentro da janela).
  - **4xx (exceto 429)**:
    - **404 no meio de uma lacuna** (`/results/{id}` inexistente): tratar como **falha dura de lacuna** (a execução deve parar imediatamente sem avançar além do último id contíguo já persistido; não “pular” o id ausente).
    - **401/403** (token inválido): **falha dura**.
- **Retries (Polly)**: somente enquanto houver janela restante suficiente para concluir e persistir.
- **Precedência de espera entre tentativas/chamadas** (do maior para o menor):
  1) `Retry-After` (quando presente e parseável);
  2) pacing mínimo do plano free: **10s** entre **inícios** de requests para a API;
  3) intervalo fixo de retry quando não houver `Retry-After`: **30s**.
- **Teto pela janela**: nenhuma espera/retry pode ultrapassar o deadline. Se ultrapassar, encerrar com parada segura.

### 13) Idempotência, concorrência e checkpoint (V0)

- **Idempotência**:
  - reexecutar a função com a mesma entrada canônica e mesma resposta da API deve produzir o mesmo blob canônico (seção 7);
  - reprocessar um `contest_id` já presente resulta em sobrescrita lógica do item (dedupe por `contest_id`).
- **Checkpoint (regra normativa)**:
  - o state **só avança** após o blob persistido refletir o último concurso persistido;
  - `LastLoadedContestId` sempre representa o **maior `contest_id` contíguo** materializado no blob.
- **Ordem obrigatória de persistência**: **blob primeiro**, **Table depois** (ver ADR 0001).
- **Concorrência (Table)**:
  - ETag é obrigatório; em conflito: parada segura.
- **Concorrência (Blob)**:
  - se houver conflito de escrita (ex.: condição/ETag do blob falha, ou outra instância reescreveu o blob entre leitura e escrita), tratar como **parada segura**; não tentar mesclar por inferência na V0.

### 14) Observabilidade (logs/métricas) — motivos de parada (V0)

A execução deve emitir logs estruturados suficientes para explicar **por que parou** e **o que (não) fez**. Todo encerramento (inclusive antecipado) deve registrar um motivo.

- **Campos mínimos recomendados em logs**:
  - `run_id` (correlação por execução), `deadline_seconds=180`, `timezone=America/Sao_Paulo`
  - `disable_business_day_guard`, `disable_20h_guard` (valores efetivos das feature toggles de calendário)
  - `reason_stop` (enum), `last_loaded_contest_id`, `latest_id`, `processed_count`, `persisted_last_id`
  - `http_attempts`, `retries_count`, `rate_limit_wait_seconds_total`, `elapsed_seconds`
- **`reason_stop` (normativo; exemplos de valores)**:
  - `EARLY_EXIT_NOT_BUSINESS_DAY`
  - `EARLY_EXIT_BEFORE_20H`
  - `EARLY_EXIT_ALREADY_LOADED_TODAY`
  - `EARLY_EXIT_ALREADY_ALIGNED` (latestId <= lastLoaded)
  - `SAFE_STOP_WINDOW_EXPIRED`
  - `SAFE_STOP_CONCURRENCY_TABLE_ETAG`
  - `SAFE_STOP_CONCURRENCY_BLOB_CONFLICT`
  - `HARD_FAIL_CONFIG_INVALID`
  - `HARD_FAIL_API_AUTH`
  - `HARD_FAIL_API_SCHEMA`
  - `HARD_FAIL_GAP_NOT_FOUND_404`
  - `HARD_FAIL_STATE_INCONSISTENT_TABLE_GT_BLOB`

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

### 6.5) Materializar a superfície pública (Azure Function Timer Trigger)

Objetivo: tornar o recorte V0 **executável no runtime normativo** (Azure Functions) sem mover semântica para o trigger.

Inclui (mínimo necessário):

- criar o projeto/pasta de function (ex.: `src/FunctionApp/`) e garantir que ele:
  - referencia o núcleo (`Application/Domain`) e a composição/infra necessárias;
  - contém `host.json` (config do host).
- implementar o handler do **Timer Trigger** (CRON normativo `0 0 * * * *`) como **orquestração**:
  - ler/validar as variáveis de ambiente **obrigatórias** (seção 2 do Contrato V0);
  - configurar DI (registrar adaptadores reais: API client, blob store, table store);
  - chamar o caso de uso do núcleo.
- observabilidade:
  - emitir logs estruturados com `reason_stop` e campos mínimos (seção 14 do Contrato V0).

Critérios objetivos de pronto (remoção de ambiguidade):

- existe um Timer Trigger real no projeto da Function (não apenas entry point para testes);
- execução local (host) é possível com as variáveis de ambiente documentadas, sem “defaults ocultos”;
- a função chama o caso de uso do núcleo (trigger fino) e não duplica regras de domínio;
- testes existentes continuam passando (o runtime não quebra o contrato do núcleo).

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

