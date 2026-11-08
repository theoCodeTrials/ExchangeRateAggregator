public record GetExchangeRatesQuery(
    string BaseCurrency,
    DateOnly RequestedDate,
    string? TargetCurrency = null,
    string? SortDirection = null);