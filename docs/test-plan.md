# Plano de testes — Lotofacil Loader (Azure Function)

Este documento define a cobertura de testes para o comportamento especificado em `docs/brief.md`, com foco em validação determinística do comportamento e dos artefatos persistidos (Blob + Table Storage).

## Objetivo do teste

Validar que uma Azure Function (Timer Trigger) em C#/.NET:

- Atualiza um **documento JSON** num blob (nome do blob: **`Lotofacil`**) contendo `draws`.
- Mantém um **estado** no Table Storage para saber o **último concurso carregado** e evitar trabalho redundante.
- Usa a API (`/results/last` e `/results/{id}`) para descobrir o último concurso disponível e preencher lacunas.
- Respeita as regras de encerramento antecipado (dia útil, 20h, já carregado hoje), a janela interna de **3 minutos**, e a política de resiliência/cadência (Polly, 429/Retry-After, 1 chamada/minuto quando aplicável).

## Fonte de verdade (recorte do spec)

- **Fontes de verdade**:
  - `docs/adrs/0001-lotofacil-loader-azure-function.md`
  - `docs/spec-driven-execution-guide.md` (seção **Contrato V0 — Lotofacil Loader (normativo)**)
  - `docs/fases-execucao-templates.md`

## Escopo e não escopo (para testes)

- **Em escopo**: leitura/gravação de Blob Storage, leitura/gravação de Table Storage (incluindo ETag como concorrência otimista), chamadas aos dois endpoints, regras de encerramento antecipado, cálculo de lacunas, janela de 3 minutos, retomada por reexecução do timer, ordem de persistência (blob antes do table), mapeamento JSON API → JSON do blob.
- **Fora do escopo** (não definido no contrato V0): geração/rotação de SAS, CI/CD, nomes de Resource Group/SKU.

## Ambiente e pré-condições de execução

### Agendamento do Timer Trigger (CRON)

O brief indica execução **a cada hora** e dá um exemplo de CRON (com segundos, formato típico de Azure Functions Timer Trigger):

- `0 0 * * * *` (a cada hora, no minuto 0)

Nos testes, deve ser possível disparar a execução (manual/forçada) e também validar que a configuração de agendamento usada no deploy corresponde ao CRON acordado (ou equivalente “a cada hora”).

### Dependências externas (devem ser controladas no teste)

- **API Lotodicas**:
  - Último concurso: `https://www.lotodicas.com.br/api/v2/lotofacil/results/last?token=<TOKEN>`
  - Concurso específico: `https://www.lotodicas.com.br/api/v2/lotofacil/results/{id}?token=<TOKEN>`
- **Azure Storage Account**:
  - **Blob Storage** (documento com `draws`, blob nomeado `Lotofacil`)
  - **Table Storage** (estado do loader; exemplo discutido: tabela `LotofacilState`, PK `Lotofacil`, RK `Loader`)

### Configuração por variáveis de ambiente (normativo na V0)

Os testes devem parametrizar a execução usando as variáveis (nomes sugeridos):

- `Lotodicas__BaseUrl`
- `Lotodicas__Token`
- `Storage__ConnectionString` (ou alternativa equivalente por nome+chave, desde que via Access Key em ambiente)
- `Storage__BlobContainer`
- `Storage__LotofacilBlobName` (esperado: `Lotofacil`)
- `Storage__LotofacilStateTable` (esperado: `LotofacilState`)

### Regras de calendário e timezone

O brief exige evitar “defaults ocultos”:

- Sorteios ocorrem **somente em dias úteis**, **às 20h**.
- A avaliação de “dia útil” e “passou das 20h” deve ocorrer na timezone normatizada no **Contrato V0** (`America/Sao_Paulo`).

Nos testes, a timezone deve ser tratada como **pré-condição do ambiente de execução** e usada para construir cenários de “antes/depois das 20h”.

## Contratos de entrada/saída (dados)

### Entrada — contrato mínimo usado da API (JSON)

Os testes devem validar o consumo da estrutura descrita no brief (campos relevantes):

- `data.draw_number` (id/concurso)
- `data.draw_date` (data do sorteio)
- `data.drawing.draw` (lista de números)
- `data.prizes[]` contendo item com `name == "15 acertos"` e campos `winners` (para derivar vencedores)

Exemplo de estrutura discutida (parcial):

- `code: 200`
- `status: "success"`
- `data: { draw_number, draw_date, drawing: { draw: [...] }, prizes: [...] }`

### Saída — documento no Blob Storage

O blob deve conter um JSON com a coleção `draws`, onde cada item segue o formato acordado:

- `contest_id` (inteiro)
- `draw_date` (string no formato usado pela API, ex.: `YYYY-MM-DD`)
- `numbers` (lista de inteiros)
- `winners_15` (inteiro)
- `has_winner_15` (boolean)

### Mapeamento (API → blob) a validar

Os testes devem validar exatamente o mapeamento descrito no brief:

- `contest_id` ← `data.draw_number`
- `draw_date` ← `data.draw_date`
- `numbers` ← `data.drawing.draw`
- `winners_15` ← em `data.prizes`, item cujo `name` é `"15 acertos"`, campo `winners`
- `has_winner_15` ← `true` se `winners_15 > 0`, senão `false`

### Saída — estado no Table Storage (lógico)

O Table Storage deve persistir o estado “último carregado”, com os campos discutidos:

- `LastLoadedContestId` (inteiro)
- `LastLoadedDrawDate` (data; derivada de `data.draw_date` do último concurso carregado)
- `LastUpdatedAtUtc` (data/hora)
- Uso de **ETag** para concorrência otimista (evitar sobrescrita inconsistente por execuções concorrentes)

## Critérios gerais de aceitação (para cada execução)

- **Encerramento antecipado** ocorre conforme as regras descritas, sem chamar a API quando não necessário.
- Quando houver novos concursos, o loader:
  - Chama `/results/last` para obter `latestId`
  - Calcula lacunas `lastLoaded + 1 ... latestId`
  - Processa ids em ordem crescente, dentro de uma janela interna de **3 minutos**
  - Em caso de não concluir todos os ids, para e **retoma** na próxima execução a partir do estado persistido
- **Ordem de persistência**: atualiza o **blob primeiro** e o **Table Storage depois** (para não marcar como carregado se falhar gravar blob).
- **Idempotência**: reexecuções do timer não corrompem dados; o alinhamento `latestId <= lastLoaded` encerra sem trabalho redundante.

## Estratégia de testes (camadas)

O brief não define uma suite automatizada existente; portanto este plano descreve **o que** deve ser validado e **como** executar cenários com dependências controladas:

- **Testes de mapeamento/contrato de dados**: validar transformação do JSON da API para o item no `draws` e a construção do documento do blob.
- **Testes de fluxo** (orquestração): validar decisões de encerramento antecipado, cálculo de lacunas, janela de 3 minutos, retomada.
- **Testes de integração controlada**: executar o fluxo completo contra:
  - API simulada (respostas determinísticas por id/last)
  - Storage (Blob + Table) controlado para observar gravações e estados

## Casos de teste (detalhados)

### A. Encerramento antecipado (sem chamadas à API)

- **A1 — hoje não é dia útil**
  - **Pré-condição**: “hoje” configurado como não-dia-útil na timezone explícita do ambiente.
  - **Entrada (Table)**: qualquer valor válido para `LastLoadedContestId`/`LastLoadedDrawDate`.
  - **Execução**: disparar a execução do Timer Trigger.
  - **Saída esperada**:
    - Não chama `/results/last`
    - Não lê/grava concursos no blob
    - Não atualiza o Table Storage

- **A2 — dia útil, antes das 20h**
  - **Pré-condição**: “hoje” dia útil e horário local < 20h (timezone explícita).
  - **Execução**: disparar a execução do Timer Trigger.
  - **Saída esperada**: igual a A1 (encerra antes de chamar a API).

- **A3 — dia útil, após 20h, e `LastLoadedDrawDate == hoje`**
  - **Pré-condição**: “hoje” dia útil, horário local ≥ 20h, e Table com `LastLoadedDrawDate` igual a hoje.
  - **Execução**: disparar a execução do Timer Trigger.
  - **Saída esperada**: encerra sem chamar `/results/last` e sem persistências.

### B. Alinhamento (1 chamada à API e encerra)

- **B1 — `latestId <= lastLoaded`**
  - **Entrada (Table)**: `LastLoadedContestId = N`
  - **Entrada (API /results/last)**: `data.draw_number = N` (ou menor)
  - **Execução**: disparar a execução do Timer Trigger (em condição que não seja encerramento antecipado).
  - **Saída esperada**:
    - Chama `/results/last` uma vez
    - Não chama `/results/{id}`
    - Não atualiza blob nem table (não há novos concursos)

### C. Atualização com lacunas (processamento de 1 ou mais ids)

- **C1 — um único concurso em falta**
  - **Entrada (Table)**: `LastLoadedContestId = N`
  - **Entrada (API /results/last)**: `data.draw_number = N+1`
  - **Entrada (API /results/{id})**: resposta determinística para `id = N+1`
  - **Execução**: disparar a execução do Timer Trigger.
  - **Saída esperada**:
    - Chama `/results/last`
    - Chama `/results/N+1`
    - Atualiza o blob `Lotofacil` com `draws` incluindo o novo item (mapeamento conforme contrato)
    - Atualiza o Table Storage com:
      - `LastLoadedContestId = N+1`
      - `LastLoadedDrawDate` igual a `data.draw_date` do concurso `N+1`
      - `LastUpdatedAtUtc` preenchido
    - Confirma **ordem**: blob atualizado antes do table

- **C2 — múltiplos concursos em falta (lacuna > 1)**
  - **Entrada (Table)**: `LastLoadedContestId = N`
  - **Entrada (API /results/last)**: `data.draw_number = N+k` com \(k>1\)
  - **Entrada (API /results/{id})**: respostas determinísticas para cada `id` em `[N+1..N+k]`
  - **Execução**: disparar a execução do Timer Trigger.
  - **Saída esperada**:
    - Chamadas a `/results/{id}` em **ordem crescente**
    - Blob atualizado com todos os itens novos em `draws`
    - Table atualizado apontando para o **último id processado**

### D. Janela interna de 3 minutos e retomada

- **D1 — não conclui todos os ids dentro da janela**
  - **Objetivo**: validar a regra “parar quando a janela expira; retomar na próxima execução”.
  - **Entrada (Table)**: `LastLoadedContestId = N`
  - **Entrada (API /results/last)**: `latestId = N+k` com lacuna suficientemente grande para não concluir
  - **Condição de execução**: induzir atraso/limitação nas chamadas (ex.: cadência/espera por 60s quando aplicável, e/ou retries) de forma que exceda 3 minutos antes de processar todos os ids.
  - **Execução**: disparar uma execução, depois disparar uma execução subsequente.
  - **Saída esperada**:
    - 1ª execução: processa um prefixo dos ids, atualiza blob e table para o último id efetivamente processado, e interrompe ao expirar a janela
    - 2ª execução: calcula lacunas a partir do `LastLoadedContestId` persistido e continua do próximo id

### E. Resiliência, rate limit e retry

- **E1 — resposta 429 com `Retry-After`**
  - **Entrada (API)**: para algum `/results/{id}`, devolver 429 com cabeçalho `Retry-After` (quando existir).
  - **Execução**: disparar a execução do Timer Trigger.
  - **Saída esperada**:
    - A política de retry respeita `Retry-After`
    - O processamento permanece dentro do limite de 3 minutos; se exceder, aplica o comportamento de D1 (parar e retomar)

- **E2 — retry com intervalo de 30s até 3 minutos**
  - **Entrada (API)**: falhas transitórias (ex.: timeouts/5xx) em `/results/{id}` que exijam retry.
  - **Execução**: disparar a execução do Timer Trigger.
  - **Saída esperada**:
    - Tentativas ocorrem com intervalo discutido (30s) e respeitando o teto de 3 minutos de trabalho na execução
    - Sem marcar concurso como carregado no Table se o blob não foi atualizado com sucesso

- **E3 — cadência de 1 chamada por minuto quando aplicável**
  - **Condição**: cenário em que a regra “1 pedido por minuto” deve ser respeitada (conforme discussão do fornecedor).
  - **Execução**: disparar a execução com múltiplos ids a carregar.
  - **Saída esperada**:
    - Entre chamadas, há espera até completar ~60s desde a última chamada (`lastApiCallUtc` como conceito discutido)
    - Se a espera impedir concluir todos os ids em 3 minutos, aplica-se a retomada (D1)

### F. Concorrência e consistência (Table ETag + ordem blob→table)

- **F1 — concorrência otimista via ETag no Table**
  - **Objetivo**: validar que duas execuções concorrentes não corrompem o estado.
  - **Cenário**: simular duas instâncias tentando atualizar o mesmo registro (PK `Lotofacil`, RK `Loader`) com ETag.
  - **Saída esperada**:
    - Em caso de conflito, o estado final no Table não retrocede para um `LastLoadedContestId` menor
    - O blob permanece consistente (documento completo gravado; sem “append” parcial)

- **F2 — falha ao gravar blob não pode avançar Table**
  - **Entrada**: induzir falha na gravação do blob na etapa de persistência.
  - **Execução**: disparar a execução do Timer Trigger para processar ao menos 1 id.
  - **Saída esperada**:
    - Blob não é atualizado
    - Table **não** deve ser atualizado para refletir o id como carregado (ordem de persistência garante isso)

## Evidências a coletar (para validação)

Como o brief cita logging estruturado como boa prática (sem fixar formato), as evidências mínimas devem ser observáveis por inspeção de Storage e contagem de chamadas:

- **Storage (Blob)**: conteúdo final do blob `Lotofacil` com `draws` atualizado conforme mapeamento.
- **Storage (Table)**: valores finais de `LastLoadedContestId`, `LastLoadedDrawDate`, `LastUpdatedAtUtc` e comportamento esperado em concorrência (ETag).
- **API**: número de chamadas e endpoints chamados por execução (incluindo se encerrou antes de chamar a API).
- **Tempo**: evidência de respeito à janela de 3 minutos (conclusão ou interrupção e retomada).

