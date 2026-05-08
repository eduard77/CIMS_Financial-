using Financials.Application.Cims;
using Financials.Application.Common;
using Financials.Application.Persistence;
using Financials.Application.Projects;
using Financials.Domain.Projects;
using NSubstitute;

namespace Financials.Application.Tests.Projects;

public class ConfirmCimsProjectCommandHandlerTests
{
    private readonly ICimsClient _cims = Substitute.For<ICimsClient>();
    private readonly IFinancialsProjectRepository _repo = Substitute.For<IFinancialsProjectRepository>();
    private readonly IFinancialsDbContext _db = Substitute.For<IFinancialsDbContext>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private ConfirmCimsProjectCommandHandler Sut() => new(_cims, _repo, _db, _clock);

    [Fact]
    public async Task Creates_FinancialsProject_when_cims_returns_a_match()
    {
        var cimsProjectId = Guid.NewGuid();
        var now = new DateTime(2026, 5, 8, 14, 0, 0, DateTimeKind.Utc);
        _clock.UtcNow.Returns(now);
        _cims.GetProjectAsync(cimsProjectId, Arg.Any<CancellationToken>())
            .Returns(new CimsProjectSummary(cimsProjectId, "Tower", "PRJ-001"));
        _repo.FindByCimsProjectIdAsync(cimsProjectId, Arg.Any<CancellationToken>())
            .Returns((FinancialsProject?)null);

        var result = await Sut().Handle(
            new ConfirmCimsProjectCommand(cimsProjectId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
        _repo.Received(1).Add(Arg.Is<FinancialsProject>(p =>
            p.CimsProjectId == cimsProjectId && p.ConfirmedAt == now));
        await _db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_failure_when_cims_does_not_have_the_project()
    {
        var cimsProjectId = Guid.NewGuid();
        _cims.GetProjectAsync(cimsProjectId, Arg.Any<CancellationToken>())
            .Returns((CimsProjectSummary?)null);

        var result = await Sut().Handle(
            new ConfirmCimsProjectCommand(cimsProjectId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("not found");
        _repo.DidNotReceive().Add(Arg.Any<FinancialsProject>());
        await _db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_failure_when_project_already_confirmed()
    {
        var cimsProjectId = Guid.NewGuid();
        _cims.GetProjectAsync(cimsProjectId, Arg.Any<CancellationToken>())
            .Returns(new CimsProjectSummary(cimsProjectId, "Tower", "PRJ-001"));
        _repo.FindByCimsProjectIdAsync(cimsProjectId, Arg.Any<CancellationToken>())
            .Returns(FinancialsProject.Confirm(cimsProjectId, DateTime.UtcNow));

        var result = await Sut().Handle(
            new ConfirmCimsProjectCommand(cimsProjectId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("already confirmed");
        _repo.DidNotReceive().Add(Arg.Any<FinancialsProject>());
    }

    [Fact]
    public async Task Returns_failure_when_cims_throws_HttpRequestException()
    {
        var cimsProjectId = Guid.NewGuid();
        _cims.GetProjectAsync(cimsProjectId, Arg.Any<CancellationToken>())
            .Returns<CimsProjectSummary?>(_ => throw new HttpRequestException("connection refused"));

        var result = await Sut().Handle(
            new ConfirmCimsProjectCommand(cimsProjectId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("CIMS is currently unavailable");
        _repo.DidNotReceive().Add(Arg.Any<FinancialsProject>());
    }

    [Fact]
    public void Validator_rejects_empty_cims_project_id()
    {
        var validator = new ConfirmCimsProjectValidator();

        var result = validator.Validate(new ConfirmCimsProjectCommand(Guid.Empty));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.PropertyName.Should().Be(nameof(ConfirmCimsProjectCommand.CimsProjectId));
    }
}
