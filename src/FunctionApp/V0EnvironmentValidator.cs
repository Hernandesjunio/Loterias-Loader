using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Lotofacil.Loader.FunctionApp;

public sealed class V0EnvironmentValidator
{
    private static readonly Regex BlobContainerRegex = new("^[a-z0-9](?:[a-z0-9-]{1,61}[a-z0-9])?$", RegexOptions.Compiled);

    private readonly IConfiguration _cfg;

    public V0EnvironmentValidator(IConfiguration cfg) => _cfg = cfg;

    public ValidationResult Validate()
    {
        // Contrato V0 — docs/spec-driven-execution-guide.md (seção 2).
        var baseUrl = (_cfg["Lotodicas:BaseUrl"] ?? _cfg["Lotodicas__BaseUrl"])?.Trim();
        var token = (_cfg["Lotodicas:Token"] ?? _cfg["Lotodicas__Token"])?.Trim();

        // Adendo V0.1 — schedule configurável por ambiente.
        var timerSchedule = (_cfg["LotofacilLoader:TimerSchedule"] ?? _cfg["LotofacilLoader__TimerSchedule"])?.Trim();

        var conn = (_cfg["Storage:ConnectionString"] ?? _cfg["Storage__ConnectionString"])?.Trim();
        var container = (_cfg["Storage:BlobContainer"] ?? _cfg["Storage__BlobContainer"])?.Trim();
        var blobName = (_cfg["Storage:LotofacilBlobName"] ?? _cfg["Storage__LotofacilBlobName"])?.Trim();
        var tableName = (_cfg["Storage:LotofacilStateTable"] ?? _cfg["Storage__LotofacilStateTable"])?.Trim();

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return ValidationResult.Invalid("Lotodicas__BaseUrl é obrigatório");
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) ||
            !string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            return ValidationResult.Invalid("Lotodicas__BaseUrl deve ser URL absoluta HTTPS");
        }

        if (baseUrl.EndsWith("/", StringComparison.Ordinal))
        {
            return ValidationResult.Invalid("Lotodicas__BaseUrl não deve terminar com '/' (normalização canônica)");
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return ValidationResult.Invalid("Lotodicas__Token é obrigatório");
        }

        if (string.IsNullOrWhiteSpace(timerSchedule))
        {
            return ValidationResult.Invalid("LotofacilLoader__TimerSchedule é obrigatório");
        }

        if (string.IsNullOrWhiteSpace(conn))
        {
            return ValidationResult.Invalid("Storage__ConnectionString é obrigatório");
        }

        if (string.IsNullOrWhiteSpace(container))
        {
            return ValidationResult.Invalid("Storage__BlobContainer é obrigatório");
        }

        if (container.Length is < 3 or > 63 || !BlobContainerRegex.IsMatch(container))
        {
            return ValidationResult.Invalid("Storage__BlobContainer deve ser um nome válido de container do Blob Storage");
        }

        if (!string.Equals(blobName, "Lotofacil", StringComparison.Ordinal))
        {
            return ValidationResult.Invalid("Storage__LotofacilBlobName deve ser 'Lotofacil' (V0 normativo)");
        }

        if (!string.Equals(tableName, "LotofacilState", StringComparison.Ordinal))
        {
            return ValidationResult.Invalid("Storage__LotofacilStateTable deve ser 'LotofacilState' (V0 normativo)");
        }

        return ValidationResult.Valid();
    }

    public readonly record struct ValidationResult(bool IsValid, string? Error)
    {
        public static ValidationResult Valid() => new(true, null);
        public static ValidationResult Invalid(string error) => new(false, error);
    }
}

