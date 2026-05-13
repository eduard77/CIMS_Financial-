using Financials.Application.Commitments;
using Financials.Application.Common;
using Financials.Application.Persistence;
using Financials.Domain.Commitments;
using Financials.Domain.Common;
using Financials.Domain.Projects;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Financials.Application.Tests.Commitments;

public class ActivateCommitmentCommandHandlerTests
{
    private readonly ICommitmentRepository _commitments = Substitute.For<ICommitmentRepository>();
    private readonly IFinancialsDbContext _db = Substitute.For<IFinancialsDbContext>();
    private readonly ICurrentUserService _user = Substitute.For<ICurrentUserService>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IOverCommitmentEvaluator _evaluator = Substitute.For<IOverCommitmentEvaluator>();

    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid CostCode = Guid.NewGuid();
    private static readonly Guid Counterparty = Guid.NewGuid();

    private ActivateCommitmentCommandHandler Sut() => new(
        _commitments, _db, _user, _clock, _evaluator, NullLogger<ActivateCommitmentCommandHandler>.Instance);

    private static Commitment NewWithLine()
    {
        var c = Commitment.Create(ProjectId, CommitmentType.Subcontract, "SC-1", Counterparty);
        c.AddLine(1, CostCode, "Excavation", 1m, "m3", Money.Gbp(100m));
        return c;
    }

    private OverCommitmentLineBreach Breach() => new(
        CostCode, Money.Gbp(80m), Money.Gbp(0m), Money.Gbp(100m), Money.Gbp(20m));

    [Fact]
    public async Task HardBlock_with_breaches_returns_failure_without_saving()
    {
        var commitment = NewWithLine();
        _user.UserId.Returns("u");
        _clock.UtcNow.Returns(DateTime.UtcNow);
        _commitments.FindByIdAsync(commitment.Id, Arg.Any<CancellationToken>()).Returns(commitment);
        _evaluator.EvaluateAsync(commitment.Id, Arg.Any<CancellationToken>())
            .Returns(new OverCommitmentEvaluation(
                OverCommitmentMode.HardBlock, Money.Gbp(0m), new[] { Breach() }));

        var result = await Sut().Handle(new ActivateCommitmentCommand(commitment.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("blocked");
        commitment.Status.Should().Be(CommitmentStatus.Draft);
        await _db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Warn_with_breaches_activates_and_saves()
    {
        var commitment = NewWithLine();
        _user.UserId.Returns("u");
        _clock.UtcNow.Returns(DateTime.UtcNow);
        _commitments.FindByIdAsync(commitment.Id, Arg.Any<CancellationToken>()).Returns(commitment);
        _evaluator.EvaluateAsync(commitment.Id, Arg.Any<CancellationToken>())
            .Returns(new OverCommitmentEvaluation(
                OverCommitmentMode.Warn, Money.Gbp(0m), new[] { Breach() }));

        var result = await Sut().Handle(new ActivateCommitmentCommand(commitment.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        commitment.Status.Should().Be(CommitmentStatus.Active);
        await _db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Disabled_with_no_breaches_activates_normally()
    {
        var commitment = NewWithLine();
        _user.UserId.Returns("u");
        _clock.UtcNow.Returns(DateTime.UtcNow);
        _commitments.FindByIdAsync(commitment.Id, Arg.Any<CancellationToken>()).Returns(commitment);
        _evaluator.EvaluateAsync(commitment.Id, Arg.Any<CancellationToken>())
            .Returns(OverCommitmentEvaluation.Clean(OverCommitmentMode.Disabled, Money.Gbp(0m)));

        var result = await Sut().Handle(new ActivateCommitmentCommand(commitment.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        commitment.Status.Should().Be(CommitmentStatus.Active);
    }

    [Fact]
    public async Task Missing_user_returns_failure()
    {
        _user.UserId.Returns(string.Empty);
        var result = await Sut().Handle(new ActivateCommitmentCommand(Guid.NewGuid()), CancellationToken.None);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Missing_commitment_returns_failure()
    {
        _user.UserId.Returns("u");
        _commitments.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Commitment?)null);
        // Evaluator should not be hit when commitment is missing — but the current handler
        // calls evaluator after finding the commitment, so this path returns the not-found
        // failure before any evaluator call.
        var result = await Sut().Handle(new ActivateCommitmentCommand(Guid.NewGuid()), CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("not found");
    }
}
