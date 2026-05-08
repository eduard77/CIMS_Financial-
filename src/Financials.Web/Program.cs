using System.Globalization;
using Financials.Application;
using Financials.Infrastructure;
using Financials.Web.Components;
using MudBlazor.Services;
using Serilog;

// Bootstrap logger captures any failure that happens before the host is built.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Financials.Web (Sprint 0 bootstrap)");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithEnvironmentName()
        .Enrich.WithThreadId());

    builder.Services
        .AddRazorComponents()
        .AddInteractiveServerComponents();

    builder.Services.AddMudServices();

    builder.Services.AddApplication();

    var connectionString = builder.Configuration.GetConnectionString("FinancialsDb")
        ?? throw new InvalidOperationException(
            "Missing connection string 'FinancialsDb'. Configure via appsettings.json, " +
            "user-secrets, or the FINANCIALS_DB_CONNECTION environment variable.");
    builder.Services.AddInfrastructure(connectionString);

    var app = builder.Build();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseStaticFiles();
    app.UseAntiforgery();

    app.UseSerilogRequestLogging();

    app.MapRazorComponents<App>()
       .AddInteractiveServerRenderMode();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Financials.Web terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

namespace Financials.Web
{
    /// <summary>
    /// Public marker so WebApplicationFactory&lt;Program&gt; can resolve the entry assembly
    /// from integration tests. Required because top-level statements emit an internal Program.
    /// </summary>
    public partial class Program;
}
