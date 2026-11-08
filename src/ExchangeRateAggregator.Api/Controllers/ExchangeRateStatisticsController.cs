using ExchangeRateAggregator.Infrastructure.Statistics;
using Microsoft.AspNetCore.Mvc;

namespace ExchangeRateAggregator.Api.Controllers;

[ApiController]
[Route("api/exchange-rates/statistics")]
public class ExchangeRateStatisticsController : ControllerBase
{
    private readonly InMemoryProviderStatisticsStore _statisticsStore;

    public ExchangeRateStatisticsController(
        InMemoryProviderStatisticsStore statisticsStore)
    {
        _statisticsStore = statisticsStore;
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ProviderRequestStatistics>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<ProviderRequestStatistics>> GetStatistics()
    {
        return Ok(_statisticsStore.GetSnapshot());
    }
}