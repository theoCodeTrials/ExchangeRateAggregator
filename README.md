# Exchange Rate Aggregator

A .NET 10 API that fetches historical exchange rates concurrently from NBP, Frankfurter, and the Fawaz Currency API. It groups quotes by provider, supports filtering and sorting by target currency, and returns available rates when some providers fail.

The Blazor app lets you compare rates and inspect per-provider request statistics.

## Prerequisites

- .NET 10 SDK
- Internet access for live exchange-rate requests
- Visual Studio with .NET 10 support and the **ASP.NET and web development** workload if you prefer to run from the IDE

The upstream providers require no API keys. Trust the local HTTPS development certificate before running the apps:

```powershell
dotnet dev-certs https --trust
```

## Run with Visual Studio

1. Open `ExchangeRateAggregator.slnx`.
2. Right-click the solution in **Solution Explorer** and select **Configure Startup Projects**.
3. Select **Multiple startup projects** and configure both projects:

   | Project | Action | Launch profile |
   | --- | --- | --- |
   | `ExchangeRateAggregator.Api` | Start | `https` |
   | `ExchangeRateAggregator.Web` | Start | `https` |

4. Apply the settings and press **F5**.

The API exposes [Swagger UI](https://localhost:7128/swagger). The [Blazor app](https://localhost:7189/) uses the API at `https://localhost:7128/`, as configured in its `appsettings.Development.json`.

## Run from the command line

From the solution root, start the API:

```powershell
dotnet run --project ./src/ExchangeRateAggregator.Api/ExchangeRateAggregator.Api.csproj --launch-profile https
```

Leave it running. In a second terminal at the solution root, start Blazor:

```powershell
dotnet run --project ./src/ExchangeRateAggregator.Web/ExchangeRateAggregator.Web.csproj --launch-profile https
```

Open the [Blazor app](https://localhost:7189/). In **Exchange rates**, look up **EUR → AUD** for **2026-11-05** and expand the provider panels. Then open **Provider statistics** and select **Refresh statistics** to see the recorded outbound requests. Provider coverage varies by currency and date, so a past weekday is a good starting point.

## API endpoints

### `GET /api/exchange-rates`

```text
GET /api/exchange-rates?baseCurrency=EUR&date=2026-11-05&targetCurrency=AUD&sortDirection=asc
```

| Query parameter | Required | Description |
| --- | --- | --- |
| `baseCurrency` | Yes | Supported three-letter base currency code, such as `EUR`. |
| `date` | Yes | Requested date in `yyyy-MM-dd` format. Future dates are rejected. |
| `targetCurrency` | No | Filter to one target currency. It must differ from the base currency. |
| `sortDirection` | No | `asc` or `desc`. Sorts quotes by target currency within each provider. |

The response contains `baseCurrency`, `requestedDate`, `providers`, `failedProviders`, and `allProvidersFailed`. Each entry in `providers` contains `providerName` and `rates`. Each quote contains `targetCurrency` and `rate`, expressed as units of the target currency per unit of the base currency.

| Status | Meaning |
| --- | --- |
| `200 OK` | At least one provider succeeded. `failedProviders` lists any providers that failed. |
| `400 Bad Request` | The query is invalid. The response contains field-specific messages in `errors`. |
| `503 Service Unavailable` | All providers failed. The rates response has `allProvidersFailed` set to `true`. |

### `GET /api/exchange-rates/statistics`

Returns `200 OK` with an array of per-provider statistics. Each entry includes the provider name, total HTTP attempts, average response time in milliseconds, fast/average/slow attempt counts, and the attempt count and average response time over the last five minutes.

Retries count as separate HTTP attempts. Cache hits make no outbound request and add no attempts. Statistics are held in memory and reset when the API restarts.

## Design and configuration

- **API** handles HTTP input and maps aggregation results to responses.
- **Application** calls the provider abstractions concurrently and applies filtering and sorting.
- **Infrastructure** implements the provider adapters, caching, retries, request statistics, and background anomaly monitoring.
- **Web** is an Interactive Server Blazor app that calls both API endpoints over HTTP.

The background monitor logs a warning when a provider’s average response time over the last five minutes is more than 50% above its lifetime average. The API’s `ExchangeRateProviders` configuration controls the upstream addresses, five-second HTTP timeout, retry settings, and one-hour cache lifetime.

## Tests

From the solution root:

```powershell
dotnet test
```

The suite includes unit tests and API integration tests. The endpoint tests use `WebApplicationFactory<Program>` with stub providers to cover validation, filtering, sorting, and partial and total provider failure without relying on live services.