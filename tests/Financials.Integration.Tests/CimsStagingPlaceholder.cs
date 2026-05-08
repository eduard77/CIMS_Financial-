namespace Financials.Integration.Tests;

/// <summary>
/// Sprint 0 placeholder so the integration ring exists and can be filtered by trait.
/// The first real CIMS-staging integration test lands in Sprint 1 alongside the
/// project-setup vertical slice.
/// </summary>
[Trait("Category", "Integration")]
public class CimsStagingPlaceholder
{
    [Fact(Skip = "Requires CIMS staging environment; first real test added in Sprint 1.")]
    public Task Will_round_trip_a_project_lookup_through_CIMS() => Task.CompletedTask;
}
