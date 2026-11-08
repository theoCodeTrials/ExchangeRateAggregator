using ExchangeRateAggregator.Application.Queries;
using ExchangeRateAggregator.Application.Validation;
using FluentAssertions;

namespace ExchangeRateAggregator.Application.Tests.Queries;

public class GetExchangeRatesQueryValidatorTests
{
    private const string SupportedBaseCurrency = "EUR";
    private const string UnsupportedBaseCurrency = "EYT";
    private const string RequiredDateMessage = "An exchange-rate date is required.";
    private const string FutureDateMessage = "The exchange-rate date cannot be in the future.";
    private const string BlankTargetCurrencyMessage = "Target currency cannot be blank.";
    private const string InvalidSortDirectionMessage = "Sort direction must be 'asc' or 'desc'.";

    private static readonly DateOnly Today = new(2026, 11, 9);
    private static readonly DateOnly PastWeekday = new(2026, 11, 6);

    private readonly GetExchangeRatesQueryValidator _validator = new();

    [Theory]
    [InlineData(SupportedBaseCurrency)]
    [InlineData("USD")]
    [InlineData("GBP")]
    [InlineData("JPY")]
    [InlineData("CHF")]
    [InlineData("CAD")]
    [InlineData("AUD")]
    [InlineData("NZD")]
    [InlineData("CNY")]
    [InlineData("SEK")]
    [InlineData("NOK")]
    [InlineData("PLN")]
    public void ValidateAcceptsSupportedBaseCurrencies(string baseCurrency)
    {
        var query = new GetExchangeRatesQuery(baseCurrency, PastWeekday);

        var validationErrors = _validator.Validate(query, Today);

        validationErrors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("eur")]
    [InlineData("eUr")]
    public void ValidateMatchesBaseCurrencyWithoutRegardToCase(string baseCurrency)
    {
        var query = new GetExchangeRatesQuery(baseCurrency, PastWeekday);

        var validationErrors = _validator.Validate(query, Today);

        validationErrors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(UnsupportedBaseCurrency)]
    [InlineData("")]
    [InlineData(" ")]
    public void ValidateReturnsABaseCurrencyErrorForUnsupportedInput(string baseCurrency)
    {
        var query = new GetExchangeRatesQuery(baseCurrency, PastWeekday);

        var validationErrors = _validator.Validate(query, Today);

        validationErrors.Should().ContainSingle().Which.Should().Be(
            new ValidationError(
                nameof(query.BaseCurrency),
                $"Base currency '{baseCurrency}' is not supported."));
    }

    [Fact]
    public void ValidateReturnsADateErrorWhenTheRequestedDateIsDefault()
    {
        var query = new GetExchangeRatesQuery(SupportedBaseCurrency, default);

        var validationErrors = _validator.Validate(query, Today);

        validationErrors.Should().ContainSingle().Which.Should().Be(
            new ValidationError(nameof(query.RequestedDate), RequiredDateMessage));
    }

    [Fact]
    public void ValidateReturnsADateErrorWhenTheRequestedDateIsInTheFuture()
    {
        var query = new GetExchangeRatesQuery(SupportedBaseCurrency, Today.AddDays(1));

        var validationErrors = _validator.Validate(query, Today);

        validationErrors.Should().ContainSingle().Which.Should().Be(
            new ValidationError(nameof(query.RequestedDate), FutureDateMessage));
    }

    [Fact]
    public void ValidateAcceptsToday()
    {
        var query = new GetExchangeRatesQuery(SupportedBaseCurrency, Today);

        var validationErrors = _validator.Validate(query, Today);

        validationErrors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    public void ValidateAcceptsPastWeekendDates(int dayOfMonth)
    {
        var weekendDate = new DateOnly(2026, 11, dayOfMonth);
        var query = new GetExchangeRatesQuery(SupportedBaseCurrency, weekendDate);

        var validationErrors = _validator.Validate(query, Today);

        validationErrors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateReturnsBothErrorsWhenTheBaseCurrencyAndRequestedDateAreInvalid()
    {
        var query = new GetExchangeRatesQuery(
            UnsupportedBaseCurrency, Today.AddDays(1));
        var expectedErrors = new[]
        {
            new ValidationError(
                nameof(query.BaseCurrency),
                $"Base currency '{UnsupportedBaseCurrency}' is not supported."),
            new ValidationError(nameof(query.RequestedDate), FutureDateMessage)
        };

        var validationErrors = _validator.Validate(query, Today);

        validationErrors.Should().BeEquivalentTo(expectedErrors);
    }

    [Fact]
    public void ValidateAcceptsAnOmittedTargetCurrency()
    {
        var query = new GetExchangeRatesQuery(
            SupportedBaseCurrency, PastWeekday, TargetCurrency: null);

        var validationErrors = _validator.Validate(query, Today);

        validationErrors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("usd")]
    [InlineData("AED")]
    [InlineData("1INCH")]
    public void ValidateAcceptsNonBlankTargetCurrencies(string targetCurrency)
    {
        var query = new GetExchangeRatesQuery(
            SupportedBaseCurrency, PastWeekday, TargetCurrency: targetCurrency);

        var validationErrors = _validator.Validate(query, Today);

        validationErrors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void ValidateReturnsATargetCurrencyErrorWhenTheFilterIsBlank(string targetCurrency)
    {
        var query = new GetExchangeRatesQuery(
            SupportedBaseCurrency, PastWeekday, TargetCurrency: targetCurrency);

        var validationErrors = _validator.Validate(query, Today);

        validationErrors.Should().ContainSingle().Which.Should().Be(
            new ValidationError(nameof(query.TargetCurrency), BlankTargetCurrencyMessage));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("asc")]
    [InlineData("desc")]
    [InlineData("ASC")]
    [InlineData("DeSc")]
    public void ValidateAcceptsAnOmittedOrSupportedSortDirection(string? sortDirection)
    {
        var query = new GetExchangeRatesQuery(
            SupportedBaseCurrency, PastWeekday, SortDirection: sortDirection);

        var validationErrors = _validator.Validate(query, Today);

        validationErrors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("descending")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void ValidateReturnsASortDirectionErrorForUnsupportedValues(string sortDirection)
    {
        var query = new GetExchangeRatesQuery(
            SupportedBaseCurrency, PastWeekday, SortDirection: sortDirection);

        var validationErrors = _validator.Validate(query, Today);

        validationErrors.Should().ContainSingle().Which.Should().Be(
            new ValidationError(nameof(query.SortDirection), InvalidSortDirectionMessage));
    }

    [Fact]
    public void ValidateReturnsAllErrorsWhenEveryFieldIsInvalid()
    {
        var query = new GetExchangeRatesQuery(
            UnsupportedBaseCurrency,
            Today.AddDays(1),
            TargetCurrency: "",
            SortDirection: "descending");
        var expectedErrors = new[]
        {
            new ValidationError(
                nameof(query.BaseCurrency),
                $"Base currency '{UnsupportedBaseCurrency}' is not supported."),
            new ValidationError(nameof(query.RequestedDate), FutureDateMessage),
            new ValidationError(nameof(query.TargetCurrency), BlankTargetCurrencyMessage),
            new ValidationError(nameof(query.SortDirection), InvalidSortDirectionMessage)
        };

        var validationErrors = _validator.Validate(query, Today);

        validationErrors.Should().BeEquivalentTo(expectedErrors);
    }

    [Theory]
    [InlineData(SupportedBaseCurrency, SupportedBaseCurrency)]
    [InlineData(SupportedBaseCurrency, "eur")]
    [InlineData("eur", SupportedBaseCurrency)]
    public void ValidateRejectsTargetCurrencyMatchingBaseCurrency(
        string baseCurrency,
        string targetCurrency)
    {
        var query = new GetExchangeRatesQuery(
            baseCurrency, PastWeekday, TargetCurrency: targetCurrency);

        var validationErrors = _validator.Validate(query, Today);

        validationErrors.Should().ContainSingle().Which.Should().Be(
            new ValidationError(
                nameof(query.TargetCurrency),
                "Target currency must differ from the base currency."));
    }
}