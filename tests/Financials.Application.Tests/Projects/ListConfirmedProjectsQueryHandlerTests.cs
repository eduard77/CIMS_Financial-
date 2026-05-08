using Financials.Application.Cims;
using Financials.Application.Projects;
using Financials.Domain.Projects;
using NSubstitute;

namespace Financials.Application.Tests.Projects;

public class ListConfirmedProjectsQueryHandlerTests
{
    private readonly IFinancialsProjectRepository _repo = Substitute.For<IFinancialsProjectRepository>();
    private readonly ICimsClient _cims = Substitute.For<ICimsClient>();

    private ListConfirmedProjectsQueryHandler Sut() => new(_repo, _cims);

    [Fact]
    public async Task Returns_empty_list_when_no_projects_confirmed()
    {
        _repo.ListAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<FinancialsProject>());

        var result = await Sut().Handle(new ListConfirmedProjectsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        await _cims.DidNotReceive().GetProjectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resolves_cims_name_and_reference_for_each_project()
    {
        var cimsId = Guid.NewGuid();
        var project = FinancialsProject.Confirm(cimsId, DateTime.UtcNow);
        _repo.ListAllAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { project });
        _cims.GetProjectAsync(cimsId, Arg.Any<CancellationToken>())
            .Returns(new CimsProjectSummary(cimsId, "Tower", "PRJ-001"));

        var result = await Sut().Handle(new ListConfirmedProjectsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                Id = project.Id,
                CimsProjectId = cimsId,
                CimsProjectName = "Tower",
                CimsProjectReference = "PRJ-001",
            });
    }

    [Fact]
    public async Task Marks_unknown_when_cims_returns_null_for_a_project()
    {
        var cimsId = Guid.NewGuid();
        var project = FinancialsProject.Confirm(cimsId, DateTime.UtcNow);
        _repo.ListAllAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { project });
        _cims.GetProjectAsync(cimsId, Arg.Any<CancellationToken>())
            .Returns((CimsProjectSummary?)null);

        var result = await Sut().Handle(new ListConfirmedProjectsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Single().CimsProjectName.Should().Contain("unknown");
    }

    [Fact]
    public async Task Returns_failure_when_cims_throws()
    {
        var cimsId = Guid.NewGuid();
        var project = FinancialsProject.Confirm(cimsId, DateTime.UtcNow);
        _repo.ListAllAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { project });
        _cims.GetProjectAsync(cimsId, Arg.Any<CancellationToken>())
            .Returns<CimsProjectSummary?>(_ => throw new HttpRequestException("dns"));

        var result = await Sut().Handle(new ListConfirmedProjectsQuery(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("CIMS is currently unavailable");
    }
}
