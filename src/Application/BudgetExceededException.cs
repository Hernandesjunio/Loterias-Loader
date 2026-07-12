namespace Lotofacil.Loader.Application;

public sealed class BudgetExceededException : Exception
{
    public BudgetExceededException(string message) : base(message)
    {
    }
}
