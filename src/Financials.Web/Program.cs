using System.Globalization;
using Financials.Application;
using Financials.Infrastructure;
using Financials.Web.Auth;
using Financials.Web.Components;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using MudBlazor.Services;
using Serilog;

// Bootstrap logger captures any failure that happens before the host is built.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Financials.Web (Sprint 1 vertical slice)");

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
    builder.Services.AddInfrastructure(connectionString, builder.Configuration);

    // ADR-0003 — CIMS-issued JWT validated locally via OIDC discovery.
    var authority = builder.Configuration["Cims:Auth:Authority"]
        ?? throw new InvalidOperationException("Cims:Auth:Authority is required (ADR-0003).");
    var audience = builder.Configuration["Cims:Auth:Audience"]
        ?? throw new InvalidOperationException("Cims:Auth:Audience is required (ADR-0003).");

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = authority;
            options.Audience = audience;
            options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
            options.MapInboundClaims = false;
            options.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
            options.TokenValidationParameters.NameClaimType = "name";

            // Blazor Server WebSocket auth — surface the access_token from the
            // negotiate query string when SignalR upgrades the connection.
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken)
                        && path.StartsWithSegments("/_blazor", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Token = accessToken;
                    }

                    return Task.CompletedTask;
                },
            };
        });

    builder.Services.AddAuthorization(options =>
    {
        foreach (var policyName in new[]
        {
            AuthorizationPolicies.ProjectsRead,
            AuthorizationPolicies.ProjectsConfirm,
            AuthorizationPolicies.SetupRead,
            AuthorizationPolicies.SetupConfigure,
        })
        {
            options.AddPolicy(
                policyName,
                policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim("permissions", policyName));
        }
    });

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

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapHealthChecks("/health");

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
