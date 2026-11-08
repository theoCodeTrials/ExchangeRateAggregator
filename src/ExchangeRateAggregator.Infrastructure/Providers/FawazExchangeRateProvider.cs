using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using ExchangeRateAggregator.Application.Models;
using ExchangeRateAggregator.Application.Providers;
using Microsoft.Extensions.Logging;

namespace ExchangeRateAggregator.Infrastructure.Providers;

public class FawazExchangeRateProvider : IExchangeRateProvider
{
    private const string CurrencyApiPackagePath = "npm/@fawazahmed0/currency-api";
    private const string CurrencyRatesPath = "v1/currencies";
    private const string JsonFileExtension = ".json";
    private const string DateFormat = "yyyy-MM-dd";
    private const string SnapshotDateProperty = "date";
    internal const string ProviderName = "Fawaz Currency API";

    private readonly HttpClient _httpClient;
    private readonly ILogger<FawazExchangeRateProvider> _logger;

    public FawazExchangeRateProvider(
        HttpClient httpClient,
        ILogger<FawazExchangeRateProvider> logger)
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
        var providerCurrencyCode = normalizedBase.ToLowerInvariant();
        var formattedDate = requestedDate.ToString(DateFormat, CultureInfo.InvariantCulture);

        try
        {
            var currencySnapshot = await FetchSnapshotAsync(
                providerCurrencyCode, formattedDate, cancellationToken);
            var snapshotRates = ReadSnapshotRates(
                currencySnapshot, normalizedBase, providerCurrencyCode, requestedDate);
            var quotes = MapQuotes(snapshotRates, normalizedBase);

            cancellationToken.ThrowIfCancellationRequested();

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

            return new ProviderRatesResult(
                Failed: true,
                BaseCurrency: normalizedBase,
                PublishedDate: null,
                Rates: Array.Empty<ExchangeRateQuote>());
        }
    }

    private Task<JsonElement> FetchSnapshotAsync(
        string providerCurrencyCode,
        string formattedDate,
        CancellationToken cancellationToken)
    {
        var escapedCurrencyCode = Uri.EscapeDataString(providerCurrencyCode);
        var requestPath = $"{CurrencyApiPackagePath}@{formattedDate}/" +
            $"{CurrencyRatesPath}/{escapedCurrencyCode}{JsonFileExtension}";

        return _httpClient.GetFromJsonAsync<JsonElement>(requestPath, cancellationToken);
    }

    private JsonElement ReadSnapshotRates(
        JsonElement currencySnapshot,
        string normalizedBase,
        string providerCurrencyCode,
        DateOnly requestedDate)
    {
        if (currencySnapshot.ValueKind != JsonValueKind.Object ||
            !currencySnapshot.TryGetProperty(SnapshotDateProperty, out var snapshotDate) ||
            snapshotDate.ValueKind != JsonValueKind.String ||
            !DateOnly.TryParseExact(
                snapshotDate.GetString(),
                DateFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var publishedDate) ||
            publishedDate != requestedDate ||
            !currencySnapshot.TryGetProperty(providerCurrencyCode, out var snapshotRates) ||
            snapshotRates.ValueKind != JsonValueKind.Object)
        {
            var formattedDate = requestedDate.ToString(DateFormat, CultureInfo.InvariantCulture);

            throw new InvalidDataException(
                $"{Name} returned an invalid snapshot for {normalizedBase} on {formattedDate}.");
        }

        return snapshotRates;
    }

    private IReadOnlyList<ExchangeRateQuote> MapQuotes(
        JsonElement snapshotRates,
        string normalizedBase)
    {
        var quotes = new List<ExchangeRateQuote>();

        foreach (var targetRate in snapshotRates.EnumerateObject())
        {
            var normalizedTarget = targetRate.Name.ToUpperInvariant();

            if (normalizedTarget == normalizedBase)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(normalizedTarget) ||
                targetRate.Value.ValueKind != JsonValueKind.Number ||
                !targetRate.Value.TryGetDecimal(out var rateValue) ||
                rateValue <= 0m)
            {
                throw new InvalidDataException(
                    $"{Name} returned an invalid rate for {normalizedBase}/{normalizedTarget}.");
            }

            quotes.Add(new ExchangeRateQuote(normalizedTarget, rateValue));
        }

        if (quotes.Count == 0)
        {
            throw new InvalidDataException(
                $"{Name} returned no usable quotes for {normalizedBase}.");
        }

        return quotes.AsReadOnly();
    }
}