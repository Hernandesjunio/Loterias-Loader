# Lotofacil-Loader

Atualizador de resultados da **Lotofácil** executado como **Azure Function** (**C# / .NET**) com **Timer Trigger**.

O sistema mantém um **JSON** num **Blob Storage** (para consumo externo via **SAS token**) e usa **Azure Table Storage** como estado para saber **qual foi o último concurso carregado**, evitando trabalho redundante e permitindo retomar atualizações em execuções futuras.

## O que este projeto faz

- **Executa por timer** (Azure Functions Timer Trigger).
- **Consulta uma API externa** para:
  - descobrir o **último concurso** publicado;
  - buscar o **resultado por concurso (id)** para preencher lacunas.
- **Atualiza um blob JSON** (nome do blob: `Lotofacil`) com uma coleção de `draws`.
- **Persiste estado no Table Storage** (último concurso carregado), para:
  - comparar “último carregado” vs “último disponível” antes de processar;
  - retomar do ponto certo se faltar tempo numa execução.

## Fonte de verdade (documentação)

Este repositório segue uma abordagem **docs-first**. As fontes de verdade são:

- `docs/adrs/0001-lotofacil-loader-azure-function.md`
- `docs/spec-driven-execution-guide.md` (inclui o **Contrato V0** normativo)
- `docs/fases-execucao-templates.md`

## Dados persistidos no blob

O blob contém um documento JSON com a chave `draws`. Cada item inclui:

- `contest_id`
- `draw_date`
- `numbers`
- `winners_15`
- `has_winner_15`

O mapeamento de campos (API → blob) e o formato completo estão detalhados em `docs/spec-driven-execution-guide.md` (Contrato V0).

## Estado no Table Storage (alto nível)

O Table Storage armazena o **último concurso carregado** para o processo de atualização.
Na conversa foram propostos (como exemplo) nomes como `LotofacilState` e um registo único de estado, incluindo `LastLoadedContestId` e `LastUpdatedAtUtc`, com uso de **ETag** para concorrência otimista.

## Restrições e comportamento (resumo)

- **Frequência do timer**: CRON **a cada hora** (ex.: `0 0 * * * *`).
- **Janela de execução**: processamento com **janela interna máxima de 3 minutos**.
- **Rate limit / resiliência**: quando houver limitação (ex.: **1 pedido/minuto**) e/ou respostas **429**, o fluxo considera **retry** (Polly) e respeito a `Retry-After` quando existir, desde que caiba na janela.
- **Ordem de persistência**: primeiro **gravar o blob**, depois **atualizar o estado** no Table Storage.

## Configuração (variáveis de ambiente)

Os nomes abaixo foram sugeridos na conversa como padrão de configuração:

- `Lotodicas__BaseUrl`
- `Lotodicas__Token`
- `Storage__ConnectionString`
- `Storage__BlobContainer`
- `Storage__LotofacilBlobName`
- `Storage__LotofacilStateTable`

Os segredos (ex.: token e credenciais do Storage) **não devem** ficar hardcoded no código-fonte.

## Hooks de Git (qualidade local)

Este repositório inclui um hook `pre-push` para **bloquear pushes** quando `dotnet test` falhar.

- **Instalar (Git Bash)**:

```bash
./scripts/install-git-hooks.sh
```

## Não-objetivos

- Não há promessa de “previsão”, “melhor chance” ou qualquer garantia de resultado.
- Não há implementação de consumo externo do blob (o consumo via SAS é responsabilidade de quem consome).

