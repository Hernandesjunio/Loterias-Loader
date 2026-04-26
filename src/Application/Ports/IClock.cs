namespace Lotofacil.Loader.Application;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

