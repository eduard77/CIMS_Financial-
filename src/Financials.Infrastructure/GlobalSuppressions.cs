using System.Diagnostics.CodeAnalysis;

// CA1812 fires on internal classes whose only construction site is reflective.
// In this assembly that is the common case rather than the exception — EF Core
// configurations via ApplyConfigurationsFromAssembly, MediatR handlers, DI-resolved
// repositories, health checks, DelegatingHandlers via AddHttpMessageHandler, and
// typed HttpClient implementations via AddHttpClient<TClient, TImpl>. Suppressing
// per class duplicated the same justification across ~26 files; an assembly-level
// suppression is more honest and cheaper to maintain (n-7 finding,
// docs/code-review-findings.md).
//
// If a new internal class genuinely is unused, the test suite will reveal it
// quickly: handlers fire integration paths, EF configurations apply during
// MigrationSmokeTests, the rest are resolved at app startup or by registered
// IHostedService background work.
[assembly: SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Scope = "module",
    Justification = "Many internal classes in this assembly are instantiated reflectively by DI / EF / MediatR / HttpClientFactory; see comment in GlobalSuppressions.cs.")]
