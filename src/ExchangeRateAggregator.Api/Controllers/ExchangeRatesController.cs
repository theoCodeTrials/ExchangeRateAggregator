using ExchangeRateAggregator.Api.Mappings;
using ExchangeRateAggregator.Api.Models;
using ExchangeRateAggregator.Application.Queries;
using ExchangeRateAggregator.Application.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace ExchangeRateAggregator.Api.Controllers;

[ApiController]
[Route("api/exchange-rates")]
public class ExchangeRatesController : ControllerBase
{
    private const string DateQueryParameter = "date";
    private const string CurrencyCodePattern = "[A-Za-z]{3}";

    private readonly GetExchangeRatesQueryHandler _queryHandler;
    private readonly ExchangeRatesResponseMapper _responseMapper;

    public ExchangeRatesController(
        GetExchangeRatesQueryHandler queryHandler,
        ExchangeRatesResponseMapper responseMapper)
    {
        _queryHandler = queryHandler;
        _responseMapper = responseMapper;
    }

    [HttpGet]
    [ProducesResponseType<ExchangeRatesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ExchangeRatesResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ExchangeRatesResponse>> GetRatesAsync(
        [FromQuery, Required, RegularExpression(CurrencyCodePattern)] string baseCurrency,
        [FromQuery(Name = DateQueryParameter), BindRequired] DateOnly requestedDate,
        [FromQuery] string? targetCurrency,
        [FromQuery] string? sortDirection,
        CancellationToken cancellationToken)
    {
        var query = new GetExchangeRatesQuery(
            baseCurrency,
            requestedDate,
            TargetCurrency: targetCurrency ?? Request.Query[nameof(targetCurrency)].FirstOrDefault(),
            SortDirection: sortDirection ?? Request.Query[nameof(sortDirection)].FirstOrDefault());

        var aggregationResult = await _queryHandler.HandleAsync(query, cancellationToken);

        if (aggregationResult.ValidationErrors.Count > 0)
        {
            return CreateValidationResponse(aggregationResult.ValidationErrors);
        }

        var response = _responseMapper.Map(query, aggregationResult);
        return CreateRatesResponse(response);
    }

    private ActionResult CreateValidationResponse(
        IReadOnlyList<ValidationError> validationErrors)
    {
        foreach (var validationError in validationErrors)
        {
            var queryParameterName = validationError.Field == nameof(GetExchangeRatesQuery.RequestedDate)
                ? DateQueryParameter
                : JsonNamingPolicy.CamelCase.ConvertName(validationError.Field);

            ModelState.AddModelError(queryParameterName, validationError.Message);
        }

        return ValidationProblem(ModelState);
    }

    private ActionResult CreateRatesResponse(ExchangeRatesResponse response)
    {
        if (response.AllProvidersFailed)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, response);
        }

        return Ok(response);
    }
}