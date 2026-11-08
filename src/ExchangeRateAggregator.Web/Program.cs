using ExchangeRateAggregator.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient("ExchangeRateApi", client =>
{
    var baseAddress = builder.Configuration["ExchangeRateApi:BaseAddress"]
        ?? throw new InvalidOperationException(
            "Configure ExchangeRateApi:BaseAddress for the Blazor app.");

    client.BaseAddress = new Uri(baseAddress, UriKind.Absolute);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();