# Glossário

Termos usados no recorte atual do `Lotofacil-Loader` (Azure Function).

## Termos (Lotofacil-Loader / Azure Function)

| Termo | Definição (curta, não preditiva) |
|---|---|
| Azure Function | Componente serverless em C#/.NET que executa o loader no Azure. |
| Timer Trigger | Agendamento CRON que dispara a função periodicamente (ex.: `0 0 * * * *`). |
| Janela de execução (3 minutos) | Limite interno de tempo por execução; processa o que couber e retoma na próxima execução. |
| Concurso (Lotofácil) | Identificador incremental do sorteio (`draw_number` na API; `contest_id` no blob). |
| Último concurso carregado (`lastLoaded`) | Estado persistido (Table Storage) indicando até qual `contest_id` o blob está atualizado. |
| Último concurso disponível (`latestId`) | Último `draw_number` retornado pela API no endpoint de “último resultado”. |
| Lacuna (gap) | Intervalo de concursos ausentes entre `lastLoaded + 1` e `latestId` a ser preenchido. |
| Documento do blob (JSON) | Artefato persistido no Blob Storage contendo a coleção `draws` com resultados normalizados. |
| `draws` | Lista no documento do blob; cada item representa um concurso e seus campos normalizados. |
| `winners_15` | Número de ganhadores de “15 acertos”, derivado de `prizes` na API. |
| `has_winner_15` | Booleano derivado; `true` quando `winners_15 > 0`. |
| Table Storage (estado) | Persistência do estado mínimo (ex.: último concurso carregado) para retomar e evitar redundância. |
| ETag / concorrência otimista | Mecanismo do Table Storage para evitar sobrescrita silenciosa em execuções concorrentes. |
| Blob Storage | Armazenamento de objetos onde o documento JSON é gravado de forma coerente (documento completo). |
| Storage Account | Recurso Azure que hospeda Blob e Table usados pelo loader. |
| SAS token | Credencial temporária para leitura externa do blob (geração/rotação fora do escopo do loader). |
| Rate limit | Restrição de cadência de chamadas imposta pela API; influencia pacing e retry. |
| HTTP 429 / `Retry-After` | Sinalização de excesso de requisições e sugestão de espera a ser respeitada. |
| Polly | Biblioteca .NET de resiliência (retry/timeout/backoff) para chamadas HTTP. |
| Pacing | Espaçamento entre chamadas (ex.: 10s) quando aplicável, respeitando a janela de execução. |
| Encerramento antecipado (pré-API) | Regras para terminar sem chamar a API (dia útil, “após 20h” em timezone explícita, sorteio do dia já carregado). |
| Timezone explícita | Configuração do ambiente para interpretar “hoje” e “20h” sem defaults ocultos. |
| Variáveis de ambiente (config/segredos) | Fonte de configuração e segredos (token, credenciais de storage), evitando hardcode. |

