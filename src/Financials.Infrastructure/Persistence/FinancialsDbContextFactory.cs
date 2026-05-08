using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Financials.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c> tooling so migrations can be added
/// from this project without bootstrapping the full Web composition root.
///
/// Connection string resolution order:
///   1. <c>FINANCIALS_DB_CONNECTION</c> environment variable.
///   2. LocalDB fallback for design-time scaffolding only — never used at runtime.
/// </summary>
public sealed class FinancialsDbContextFactory : IDesignTimeDbContextFactory<FinancialsDbContext>
{
    private const string LocalDbFallback =
        "Server=(localdb)\\MSSQLLocalDB;Database=FinancialsDesignTime;Trusted_Connection=True;Encrypt=False";

    public FinancialsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("FINANCIALS_DB_CONNECTION")
            ?? LocalDbFallback;

        var options = new DbContextOptionsBuilder<FinancialsDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(FinancialsDbContext).Assembly.GetName().Name))
            .Options;

        return new FinancialsDbContext(options);
    }
}
