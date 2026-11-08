using ExchangeRateAggregator.Api.Bootstrapping;
using ExchangeRateAggregator.Infrastructure.Bootstrapping;

var builder = WebApplication.CreateBuilder(args);

builder.AddExchangeRateApi();
builder.Services.AddControllers();
builder.Services.AddExchangeRateOpenApi();
builder.Services.AddExchangeRatePerformanceMonitoring();

var app = builder.Build();

app.UseHttpsRedirection();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Exchange Rate Aggregator v1");
    });
}

app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program { }