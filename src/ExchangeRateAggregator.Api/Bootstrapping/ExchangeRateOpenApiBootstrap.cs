using Microsoft.OpenApi;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ExchangeRateAggregator.Api.Bootstrapping;

internal static class ExchangeRateOpenApiBootstrap
{
    private const string ExchangeRatesPath = "api/exchange-rates";
    private const string DateQueryParameter = "date";
    private const string DateFormat = "yyyy-MM-dd";
    private const string AscendingSortDirection = "asc";
    private const string DescendingSortDirection = "desc";

    private static readonly string SortDirectionQueryParameter =
        JsonNamingPolicy.CamelCase.ConvertName(nameof(GetExchangeRatesQuery.SortDirection));

    public static IServiceCollection AddExchangeRateOpenApi(
        this IServiceCollection services)
    {
        return services.AddOpenApi(options =>
        {
            options.AddOperationTransformer((operation, context, _) =>
            {
                if (context.Description.RelativePath == ExchangeRatesPath)
                {
                    ConfigureQueryParameters(operation);
                }

                return Task.CompletedTask;
            });
        });
    }

    private static void ConfigureQueryParameters(OpenApiOperation operation)
    {
        if (operation.Parameters is not { } parameters)
        {
            return;
        }

        foreach (var parameter in parameters)
        {
            if (parameter is not OpenApiParameter queryParameter ||
                queryParameter.In != ParameterLocation.Query)
            {
                continue;
            }

            if (queryParameter.Name == DateQueryParameter)
            {
                ConfigureDateParameter(queryParameter);
            }
            else if (queryParameter.Name == SortDirectionQueryParameter)
            {
                ConfigureSortDirectionParameter(queryParameter);
            }
        }
    }

    private static void ConfigureDateParameter(OpenApiParameter parameter)
    {
        var currentDate = DateOnly.FromDateTime(DateTime.UtcNow);

        parameter.Description = "Exchange-rate date in YYYY-MM-DD format.";
        parameter.Example = JsonValue.Create(
            currentDate.ToString(DateFormat, CultureInfo.InvariantCulture));
    }

    private static void ConfigureSortDirectionParameter(OpenApiParameter parameter)
    {
        parameter.Description =
            "Sorts quotes by target currency within each provider. Omit to keep the original order.";

        if (parameter.Schema is OpenApiSchema schema)
        {
            schema.Enum =
            [
                JsonValue.Create(AscendingSortDirection)!,
                JsonValue.Create(DescendingSortDirection)!
            ];
        }
    }
}