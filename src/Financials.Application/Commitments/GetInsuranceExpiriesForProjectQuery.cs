using Financials.Application.Common;
using Financials.Domain.Commitments;
using MediatR;

namespace Financials.Application.Commitments;

public sealed record GetInsuranceExpiriesForProjectQuery(Guid FinancialsProjectId)
    : IRequest<Result<IReadOnlyList<InsuranceExpiryDto>>>;

public sealed record InsuranceExpiryDto(
    Guid InsuranceId,
    Guid CommitmentId,
    InsuranceCategory Category,
    string SubType,
    string Issuer,
    DateTime ExpiresAt,
    int DaysUntilExpiry,
    string AlertLevel);

public sealed class GetInsuranceExpiriesForProjectQueryHandler
    : IRequestHandler<GetInsuranceExpiriesForProjectQuery, Result<IReadOnlyList<InsuranceExpiryDto>>>
{
    private readonly ICommitmentInsuranceRepository _insurances;
    private readonly IClock _clock;

    public GetInsuranceExpiriesForProjectQueryHandler(
        ICommitmentInsuranceRepository insurances,
        IClock clock)
    {
        _insurances = insurances;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<InsuranceExpiryDto>>> Handle(
        GetInsuranceExpiriesForProjectQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var insurances = await _insurances
            .ListActiveByFinancialsProjectIdAsync(request.FinancialsProjectId, cancellationToken)
            .ConfigureAwait(false);

        var now = _clock.UtcNow;
        var dtos = insurances
            .Select(i =>
            {
                var days = (int)Math.Floor((i.ExpiresAt - now).TotalDays);
                var level = days < 7 ? "Critical"
                    : days < 14 ? "Warning"
                    : days < 30 ? "Info"
                    : "Ok";
                return new InsuranceExpiryDto(
                    i.Id, i.CommitmentId, i.Category, i.SubType, i.Issuer,
                    i.ExpiresAt, days, level);
            })
            .Where(dto => dto.AlertLevel != "Ok")
            .OrderBy(dto => dto.DaysUntilExpiry)
            .ToList();

        return Result<IReadOnlyList<InsuranceExpiryDto>>.Success(dtos);
    }
}
