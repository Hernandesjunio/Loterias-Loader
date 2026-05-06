namespace Lotofacil.Loader.Infrastructure;

public sealed class CalendarGuardsTogglesOptions
{
    public const string SectionName = "LotofacilLoader";

    public bool DisableBusinessDayGuard { get; init; } = false;
    public bool Disable20hGuard { get; init; } = false;
}

