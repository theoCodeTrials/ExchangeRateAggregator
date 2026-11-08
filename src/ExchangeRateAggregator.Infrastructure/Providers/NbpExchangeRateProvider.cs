using ExchangeRateAggregator.Application.Models;
using ExchangeRateAggregator.Application.Providers;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace ExchangeRateAggregator.Infrastructure.Providers;

public class NbpExchangeRateProvider : IExchangeRateProvider
{
    private const string TableAPath = "api/exchangerates/tables/A";
    private const string DateFormat = "yyyy-MM-dd";
    private const string JsonFormatQuery = "format=json";
    private const string PolishZlotyCode = "PLN";
    internal const string ProviderName = "NBP";

    private readonly HttpClient _httpClient;
    private readonly ILogger<NbpExchangeRateProvider> _logger;

    public NbpExchangeRateProvider(
        HttpClient httpClient,
        ILogger<NbpExchangeRateProvider> logger)
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

        try
        {
            var tableSnapshot = await FetchTableAsync(requestedDate, cancellationToken);
            var quotes = RebaseRates(tableSnapshot, normalizedBase);

            cancellationToken.ThrowIfCancellationRequested();

            return new ProviderRatesResult(
                Failed: false,
                BaseCurrency: normalizedBase,
                PublishedDate: tableSnapshot.EffectiveDate,
                Rates: quotes);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or JsonException or InvalidDataException
                or OperationCanceledException or TimeoutException or OverflowException)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogWarning(
                exception,
                "Exchange rate provider {ProviderName} failed for {BaseCurrency} on {RequestedDate}",
                Name,
                normalizedBase,
                requestedDate);

            return new ProviderRatesResult(
                Failed: true,
                BaseCurrency: normalizedBase,
                PublishedDate: null,
                Rates: Array.Empty<ExchangeRateQuote>());
        }
    }

    private async Task<NbpTable> FetchTableAsync(DateOnly requestedDate, CancellationToken cancellationToken)
    {
        var formattedDate = requestedDate.ToString(DateFormat, CultureInfo.InvariantCulture);
        var requestPath = $"{TableAPath}/{formattedDate}/?{JsonFormatQuery}";

        var tableSnapshots = await _httpClient.GetFromJsonAsync<NbpTable[]>(
            requestPath, cancellationToken);

        if (tableSnapshots is not [{ Rates.Length: > 0 } tableSnapshot] ||
            tableSnapshot.EffectiveDate != requestedDate)
        {
            throw new InvalidDataException(
                $"{Name} returned no usable Table A snapshot for {formattedDate}.");
        }

        return tableSnapshot;
    }

    private IReadOnlyList<ExchangeRateQuote> RebaseRates(
        NbpTable tableSnapshot,
        string normalizedBase)
    {
        var plnPerCurrency = BuildPlnRateLookup(tableSnapshot);

        if (!plnPerCurrency.TryGetValue(normalizedBase, out var plnPerBaseCurrency))
        {
            throw new InvalidDataException(
                $"{Name} did not return a rate for {normalizedBase}.");
        }

        var quotes = plnPerCurrency
            .Where(currencyRate => currencyRate.Key != normalizedBase)
            .Select(currencyRate => new ExchangeRateQuote(
                currencyRate.Key,
                plnPerBaseCurrency / currencyRate.Value))
            .ToArray();

        if (quotes.Length == 0)
        {
            throw new InvalidDataException(
                $"{Name} returned no usable quotes for {normalizedBase}.");
        }

        return quotes;
    }

    private Dictionary<string, decimal> BuildPlnRateLookup(NbpTable tableSnapshot)
    {
        var plnPerCurrency = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            [PolishZlotyCode] = 1m
        };

        foreach (var tableRate in tableSnapshot.Rates)
        {
            if (tableRate is null ||
                string.IsNullOrWhiteSpace(tableRate.Code) ||
                tableRate.Mid <= 0m)
            {
                throw new InvalidDataException($"{Name} returned an invalid Table A rate.");
            }

            var normalizedCurrencyCode = tableRate.Code.ToUpperInvariant();

            if (!plnPerCurrency.TryAdd(normalizedCurrencyCode, tableRate.Mid))
            {
                throw new InvalidDataException(
                    $"{Name} returned a duplicate Table A rate for {normalizedCurrencyCode}.");
            }
        }

        return plnPerCurrency;
    }

    private record NbpTable(DateOnly EffectiveDate, NbpRate[] Rates);

    private record NbpRate(string Code, decimal Mid);
}