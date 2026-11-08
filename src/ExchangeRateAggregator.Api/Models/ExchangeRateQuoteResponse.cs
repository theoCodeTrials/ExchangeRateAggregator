namespace ExchangeRateAggregator.Api.Models;

public record ExchangeRateQuoteResponse(
    string TargetCurrency,
    decimal Rate);