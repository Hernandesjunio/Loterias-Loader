using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Lotofacil.Loader.FunctionApp;

public sealed class V0EnvironmentValidator
{
    private static readonly Regex BlobContainerRegex = new("^[a-z0-9](?:[a-z0-9-]{1,61}[a-z0-9])?$", RegexOptions.Compiled);
    private static readonly Regex BlobNameRegex = new("^[A-Za-z0-9._-]{1,255}$", RegexOptions.Compiled);
    private static readonly Regex TableNameRegex = new("^[A-Za-z][A-Za-z0-9]{2,62}$", RegexOptions.Compiled);

    private readonly IConfiguration _cfg;

    public V0EnvironmentValidator(IConfiguration cfg) => _cfg = cfg;

    public ValidationResult Validate()
    {
        var baseUrl = (_cfg["Lotodicas:BaseUrl"] ?? _cfg["Lotodicas__BaseUrl"])?.Trim();
        var token = (_cfg["Lotodicas:Token"] ?? _cfg["Lotodicas__Token"])?.Trim();

        var timerSchedule =
            (_cfg["LoteriasLoader:TimerSchedule"] ?? _cfg["LoteriasLoader__TimerSchedule"]
                ?? _cfg["LotofacilLoader:TimerSchedule"] ?? _cfg["LotofacilLoader__TimerSchedule"])?.Trim();

        var conn = (_cfg["Storage:ConnectionString"] ?? _cfg["Storage__ConnectionString"])?.Trim();
        var container = (_cfg["Storage:BlobContainer"] ?? _cfg["Storage__BlobContainer"])?.Trim();
        var lotofacilBlob = (_cfg["Storage:LotofacilBlobName"] ?? _cfg["Storage__LotofacilBlobName"])?.Trim();
        var megasenaBlob = (_cfg["Storage:MegasenaBlobName"] ?? _cfg["Storage__MegasenaBlobName"])?.Trim();
        var tableName = (_cfg["Storage:LoteriasStateTable"] ?? _cfg["Storage__LoteriasStateTable"])?.Trim();

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
            return ValidationResult.Invalid("LoteriasLoader__TimerSchedule é obrigatório (ou use LotofacilLoader__TimerSchedule como compatibilidade)");
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

        if (string.IsNullOrWhiteSpace(lotofacilBlob) || !BlobNameRegex.IsMatch(lotofacilBlob))
        {
            return ValidationResult.Invalid("Storage__LotofacilBlobName é obrigatório e deve ser um nome de blob válido (1–255, letras, números, '.', '_' ou '-')");
        }

        if (string.IsNullOrWhiteSpace(megasenaBlob) || !BlobNameRegex.IsMatch(megasenaBlob))
        {
            return ValidationResult.Invalid("Storage__MegasenaBlobName é obrigatório e deve ser um nome de blob válido (1–255, letras, números, '.', '_' ou '-')");
        }

        if (string.IsNullOrWhiteSpace(tableName) || !TableNameRegex.IsMatch(tableName))
        {
            return ValidationResult.Invalid("Storage__LoteriasStateTable é obrigatório e deve ser um nome válido de tabela");
        }

        if (!string.Equals(tableName, "LoteriasState", StringComparison.Ordinal))
        {
            return ValidationResult.Invalid("Storage__LoteriasStateTable deve ser 'LoteriasState'");
        }

        return ValidationResult.Valid();
    }

    public readonly record struct ValidationResult(bool IsValid, string? Error)
    {
        public static ValidationResult Valid() => new(true, null);
        public static ValidationResult Invalid(string error) => new(false, error);
    }
}
