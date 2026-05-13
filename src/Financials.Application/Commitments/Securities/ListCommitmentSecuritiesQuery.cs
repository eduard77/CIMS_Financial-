using Financials.Application.Cims;
using Financials.Application.Common;
using Financials.Domain.Commitments;
using MediatR;

namespace Financials.Application.Commitments.Securities;

/// <summary>
/// Project-wide list of bonds / warranties / insurances across every
/// commitment, with the expiry alert level computed against today.
/// </summary>
public sealed record ListCommitmentSecuritiesQuery(Guid FinancialsProjectId)
    : IRequest<Result<IReadOnlyList<CommitmentSecurityDto>>>;

public sealed record CommitmentSecurityDto(
    Guid Id,
    Guid CommitmentId,
    SecurityType Type,
    string Reference,
    Guid? IssuerCimsOrganisationId,
    string? IssuerName,
    decimal? ValueAmount,
    string? ValueCurrency,
    DateOnly EffectiveFrom,
    DateOnly ExpiresOn,
    CommitmentSecurityStatus Status,
    CommitmentSecurityAlertLevel AlertLevel,
    int RemainingDays,
    Guid? SupersededBySecurityId,
    string? CancellationReason);

public sealed class ListCommitmentSecuritiesQueryHandler
    : IRequestHandler<ListCommitmentSecuritiesQuery, Result<IReadOnlyList<CommitmentSecurityDto>>>
{
    private readonly ICommitmentSecurityRepository _securities;
    private readonly ICimsClient _cims;
    private readonly IClock _clock;

    public ListCommitmentSecuritiesQueryHandler(
        ICommitmentSecurityRepository securities,
        ICimsClient cims,
        IClock clock)
    {
        _securities = securities;
        _cims = cims;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<CommitmentSecurityDto>>> Handle(
        ListCommitmentSecuritiesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var securities = await _securities
            .ListByFinancialsProjectIdAsync(request.FinancialsProjectId, cancellationToken)
            .ConfigureAwait(false);
        if (securities.Count == 0)
        {
            return Result<IReadOnlyList<CommitmentSecurityDto>>.Success(
                Array.Empty<CommitmentSecurityDto>());
        }

        var today = DateOnly.FromDateTime(_clock.UtcNow);
        var issuerNames = new Dictionary<Guid, string?>();
        try
        {
            foreach (var s in securities)
            {
                if (s.IssuerCimsOrganisationId is { } issuerId && !issuerNames.ContainsKey(issuerId))
                {
                    var org = await _cims
                        .GetOrganisationAsync(issuerId, cancellationToken)
                        .ConfigureAwait(false);
                    issuerNames[issuerId] = org?.Name;
                }
            }
        }
        catch (HttpRequestException)
        {
            return Result<IReadOnlyList<CommitmentSecurityDto>>.Failure(
                "CIMS is currently unavailable. Issuer details cannot be displayed.");
        }

        var dtos = new List<CommitmentSecurityDto>(securities.Count);
        foreach (var s in securities)
        {
            var alert = s.Status == CommitmentSecurityStatus.Active
                ? CommitmentSecurityAlertWindow.Compute(today, s.ExpiresOn)
                : CommitmentSecurityAlertLevel.None;
            var remaining = s.ExpiresOn.DayNumber - today.DayNumber;

            dtos.Add(new CommitmentSecurityDto(
                s.Id,
                s.CommitmentId,
                s.Type,
                s.Reference,
                s.IssuerCimsOrganisationId,
                s.IssuerCimsOrganisationId.HasValue
                    ? issuerNames.GetValueOrDefault(s.IssuerCimsOrganisationId.Value)
                    : null,
                s.Value?.Amount,
                s.Value?.Currency,
                s.EffectiveFrom,
                s.ExpiresOn,
                s.Status,
                alert,
                remaining,
                s.SupersededBySecurityId,
                s.CancellationReason));
        }

        return Result<IReadOnlyList<CommitmentSecurityDto>>.Success(dtos);
    }
}
