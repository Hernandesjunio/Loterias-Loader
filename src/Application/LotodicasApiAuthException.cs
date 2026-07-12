namespace Lotofacil.Loader.Application;

public sealed class LotodicasApiAuthException : Exception
{
    public LotodicasApiAuthException(int statusCode, string path)
        : base($"Lotodicas API returned {statusCode} for {path}.")
    {
        StatusCode = statusCode;
        Path = path;
    }

    public int StatusCode { get; }

    public string Path { get; }
}
