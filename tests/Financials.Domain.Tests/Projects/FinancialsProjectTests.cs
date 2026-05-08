using Financials.Domain.Projects;

namespace Financials.Domain.Tests.Projects;

public class FinancialsProjectTests
{
    [Fact]
    public void Confirm_assigns_id_cims_project_id_and_confirmed_at()
    {
        var cimsProjectId = Guid.NewGuid();
        var confirmedAt = new DateTime(2026, 5, 8, 12, 0, 0, DateTimeKind.Utc);

        var project = FinancialsProject.Confirm(cimsProjectId, confirmedAt);

        project.Id.Should().NotBeEmpty();
        project.CimsProjectId.Should().Be(cimsProjectId);
        project.ConfirmedAt.Should().Be(confirmedAt);
    }

    [Fact]
    public void Confirm_forces_confirmed_at_to_utc_kind_even_when_caller_passes_unspecified()
    {
        var cimsProjectId = Guid.NewGuid();
        var unspecified = new DateTime(2026, 5, 8, 12, 0, 0, DateTimeKind.Unspecified);

        var project = FinancialsProject.Confirm(cimsProjectId, unspecified);

        project.ConfirmedAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Confirm_throws_when_cims_project_id_is_empty()
    {
        var act = () => FinancialsProject.Confirm(Guid.Empty, DateTime.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("cimsProjectId");
    }

    [Fact]
    public void Two_confirmed_projects_have_distinct_ids()
    {
        var first = FinancialsProject.Confirm(Guid.NewGuid(), DateTime.UtcNow);
        var second = FinancialsProject.Confirm(Guid.NewGuid(), DateTime.UtcNow);

        first.Id.Should().NotBe(second.Id);
    }

    [Fact]
    public void Audit_columns_are_default_until_interceptor_stamps_them()
    {
        var project = FinancialsProject.Confirm(Guid.NewGuid(), DateTime.UtcNow);

        project.CreatedAt.Should().Be(default);
        project.CreatedByUserId.Should().BeEmpty();
        project.UpdatedAt.Should().Be(default);
        project.UpdatedByUserId.Should().BeEmpty();
    }
}
