using ExchangeRateAggregator.Application.Validation;

namespace ExchangeRateAggregator.Application.Queries;

public class GetExchangeRatesQueryValidator
{
    private const string AscendingSortDirection = "asc";
    private const string DescendingSortDirection = "desc";

    private static readonly HashSet<string> SupportedBaseCurrencies =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "EUR", "USD", "GBP", "JPY", "CHF", "CAD",
            "AUD", "NZD", "CNY", "SEK", "NOK", "PLN"
        };

    public IReadOnlyList<ValidationError> Validate(
        GetExchangeRatesQuery query,
        DateOnly today)
    {
        var errors = new List<ValidationError>();

        ValidateBaseCurrency(query, errors);
        ValidateRequestedDate(query, today, errors);
        ValidateTargetCurrency(query, errors);
        ValidateSortDirection(query, errors);

        return errors.AsReadOnly();
    }

    private static void ValidateBaseCurrency(
        GetExchangeRatesQuery query,
        List<ValidationError> errors)
    {
        if (!SupportedBaseCurrencies.Contains(query.BaseCurrency))
        {
            errors.Add(new ValidationError(
                nameof(query.BaseCurrency),
                $"Base currency '{query.BaseCurrency}' is not supported."));
        }
    }

    private static void ValidateRequestedDate(
        GetExchangeRatesQuery query,
        DateOnly today,
        List<ValidationError> errors)
    {
        if (query.RequestedDate == default)
        {
            errors.Add(new ValidationError(
                nameof(query.RequestedDate),
                "An exchange-rate date is required."));
            return;
        }

        if (query.RequestedDate > today)
        {
            errors.Add(new ValidationError(
                nameof(query.RequestedDate),
                "The exchange-rate date cannot be in the future."));
        }
    }

    private static void ValidateTargetCurrency(
        GetExchangeRatesQuery query,
        List<ValidationError> errors)
    {
        var targetCurrency = query.TargetCurrency;

        if (targetCurrency is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(targetCurrency))
        {
            errors.Add(new ValidationError(nameof(query.TargetCurrency), "Target currency cannot be blank."));
            return;
        }

        if (string.Equals(query.BaseCurrency, targetCurrency, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new ValidationError(nameof(query.TargetCurrency), "Target currency must differ from the base currency."));
        }
    }

    private static void ValidateSortDirection(
        GetExchangeRatesQuery query,
        List<ValidationError> errors)
    {
        var sortDirection = query.SortDirection;

        if (sortDirection is null)
        {
            return;
        }

        if (!string.Equals(sortDirection, AscendingSortDirection, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(sortDirection, DescendingSortDirection, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new ValidationError(
                nameof(query.SortDirection),
                $"Sort direction must be '{AscendingSortDirection}' or '{DescendingSortDirection}'."));
        }
    }
}