using System.Reflection;
using Xunit;

namespace ContractTests.V0;

internal static class SutContract
{
    /// <summary>
    /// Contract for V0 implementation discovery.
    ///
    /// These contract tests intentionally do NOT implement any production logic.
    /// They look for a production assembly (built elsewhere) and fail with a clear,
    /// spec-referenced message if it's not available yet.
    /// </summary>
    internal static Assembly RequireSutAssembly()
    {
        var path = Environment.GetEnvironmentVariable("LOTOfacil_LOADER_V0_ASSEMBLY_PATH");

        if (string.IsNullOrWhiteSpace(path))
        {
            Assert.Fail(
                """
                Falha de contrato (V0): a implementação ainda não está disponível para estes testes.

                Para executar estes testes contra a V0, compile a Azure Function/.NET e informe o caminho do assembly via:
                  - variável de ambiente `LOTOfacil_LOADER_V0_ASSEMBLY_PATH` (caminho absoluto para o .dll)

                Referência obrigatória: `docs/spec-driven-execution-guide.md`, seção "Contrato V0 — Lotofacil Loader (normativo)".

                Observação: esta falha é intencional (testes vermelhos primeiro). Não implemente lógica aqui.
                """
            );
        }

        if (!File.Exists(path))
        {
            Assert.Fail(
                $"""
                Falha de contrato (V0): `LOTOfacil_LOADER_V0_ASSEMBLY_PATH` aponta para um arquivo inexistente.

                Valor atual: `{path}`

                Este teste deve falhar por ausência de implementação até a V0 existir.
                Quando existir, ajuste a variável para o .dll compilado da V0.
                """
            );
        }

        return Assembly.LoadFrom(path);
    }

    internal static Type RequireEntryPointType(Assembly sut)
    {
        // Deliberately strict: forces a stable public surface for contract tests.
        // This is not "production logic"; it's the minimum discoverable entrypoint.
        const string expectedTypeName = "Lotofacil.Loader.V0.Contract.EntryPoint";

        var t = sut.GetType(expectedTypeName, throwOnError: false, ignoreCase: false);
        if (t is null)
        {
            Assert.Fail(
                $"""
                Falha de contrato (V0): tipo de entrada não encontrado no assembly da V0.

                Esperado: tipo público `{expectedTypeName}`.

                Este tipo é o ponto de acoplamento mínimo para os testes de contrato chamarem a V0 sem Azure.
                Ele deve materializar o comportamento descrito em `docs/spec-driven-execution-guide.md`:
                  - encerramentos antecipados (seção 10)
                  - alinhamento latestId<=lastLoaded (seção 10/11)
                  - lacunas + janela (seção 11)
                  - resiliência 429/Retry-After + teto pela janela (seção 12)
                  - persistência blob→table + ETag safe (seção 13)
                """
            );
        }

        return t;
    }
}

