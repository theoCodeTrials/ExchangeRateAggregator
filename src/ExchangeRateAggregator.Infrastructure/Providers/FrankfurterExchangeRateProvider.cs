using ExchangeRateAggregator.Application.Models;
using ExchangeRateAggregator.Application.Providers;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace ExchangeRateAggregator.Infrastructure.Providers;

public class FrankfurterExchangeRateProvider : IExchangeRateProvider
{
    private const string RatesPath = "v2/rates";
    private const string DateFormat = "yyyy-MM-dd";
    private const string BaseCurrencyParameter = "base";
    private const string DateParameter = "date";
    private const string ProvidersParameter = "providers";
    private const string EcbProviderCode = "ecb";
    internal const string ProviderName = "Frankfurter";

    private readonly HttpClient _httpClient;
    private readonly ILogger<FrankfurterExchangeRateProvider> _logger;

    public FrankfurterExchangeRateProvider(
        HttpClient httpClient,
        ILogger<FrankfurterExchangeRateProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public string Name => ProviderName;

    public async Task<ProviderRatesResult> GetRatesAsync(
        string baseCurrency,
        DateOnly requestedDate,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedBase = baseCurrency.ToUpperInvariant();
        var formattedDate = requestedDate.ToString(DateFormat, CultureInfo.InvariantCulture);

        try
        {
            var providerRates = await FetchRatesAsync(
                normalizedBase, formattedDate, cancellationToken);
            var quotes = MapQuotes(providerRates, normalizedBase, requestedDate);

            cancellationToken.ThrowIfCancellationRequested();

            if (quotes.Count == 0)
            {
                _logger.LogWarning(
                    "Exchange rate provider {ProviderName} returned no usable quotes for {BaseCurrency} on {RequestedDate}",
                    Name,
                    normalizedBase,
                    formattedDate);

                return FailedResult(normalizedBase);
            }

            return new ProviderRatesResult(
                Failed: false,
                BaseCurrency: normalizedBase,
                PublishedDate: requestedDate,
                Rates: quotes);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or JsonException or InvalidDataException
                or OperationCanceledException or TimeoutException)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogWarning(
                exception,
                "Exchange rate provider {ProviderName} failed for {BaseCurrency} on {RequestedDate}",
                Name,
                normalizedBase,
                formattedDate);

            return FailedResult(normalizedBase);
        }
    }

    private async Task<FrankfurterRate[]> FetchRatesAsync(
        string normalizedBase,
        string formattedDate,
        CancellationToken cancellationToken)
    {
        var escapedBaseCurrency = Uri.EscapeDataString(normalizedBase);
        var requestPath = $"{RatesPath}?{BaseCurrencyParameter}={escapedBaseCurrency}" +
            $"&{DateParameter}={formattedDate}&{ProvidersParameter}={EcbProviderCode}";

        return await _httpClient.GetFromJsonAsync<FrankfurterRate[]>(
            requestPath, cancellationToken)
            ?? throw new InvalidDataException(
                $"{Name} returned a null rates payload for {normalizedBase} on {formattedDate}.");
    }

    private IReadOnlyList<ExchangeRateQuote> MapQuotes(
        IReadOnlyList<FrankfurterRate> providerRates,
        string normalizedBase,
        DateOnly requestedDate)
    {
        var quotes = new List<ExchangeRateQuote>();

        foreach (var providerRate in providerRates)
        {
            if (providerRate is null ||
                !string.Equals(providerRate.Base, normalizedBase, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(providerRate.Quote) ||
                providerRate.Rate <= 0m ||
                providerRate.Date == default)
            {
                throw new InvalidDataException(
                    $"{Name} returned an invalid rate for {normalizedBase}.");
            }

            var normalizedTarget = providerRate.Quote.ToUpperInvariant();

            if (providerRate.Date != requestedDate || normalizedTarget == normalizedBase)
            {
                continue;
            }

            quotes.Add(new ExchangeRateQuote(normalizedTarget, providerRate.Rate));
        }

        return quotes.AsReadOnly();
    }

    private static ProviderRatesResult FailedResult(string normalizedBase) => new ProviderRatesResult(
            Failed: true,
            BaseCurrency: normalizedBase,
            PublishedDate: null,
            Rates: Array.Empty<ExchangeRateQuote>());

    private record FrankfurterRate(
        DateOnly Date,
        string Base,
        string Quote,
        decimal Rate);
}