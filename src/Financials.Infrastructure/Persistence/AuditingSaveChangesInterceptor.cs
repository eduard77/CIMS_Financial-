using System.Diagnostics.CodeAnalysis;
using Financials.Application.Common;
using Financials.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Financials.Infrastructure.Persistence;

/// <summary>
/// Stamps the four <see cref="IAuditable"/> columns on Added and Modified
/// entities at SaveChanges time. Per ADR-0004: audit columns are set here,
/// never by hand. A null <see cref="ICurrentUserService.UserId"/> on a save
/// that touches an <see cref="IAuditable"/> is a fault, not a default-to-system
/// fallback (CLAUDE.md §2 #8).
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Resolved by the DI container and attached via AddDbContext options.")]
internal sealed class AuditingSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IClock _clock;
    private readonly ICurrentUserService _currentUser;

    public AuditingSaveChangesInterceptor(IClock clock, ICurrentUserService currentUser)
    {
        _clock = clock;
        _currentUser = currentUser;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = _clock.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    var addUser = RequireUserId(entry);
                    SetCurrent(entry, nameof(IAuditable.CreatedAt), now);
                    SetCurrent(entry, nameof(IAuditable.CreatedByUserId), addUser);
                    SetCurrent(entry, nameof(IAuditable.UpdatedAt), now);
                    SetCurrent(entry, nameof(IAuditable.UpdatedByUserId), addUser);
                    break;

                case EntityState.Modified:
                    var modUser = RequireUserId(entry);
                    SetCurrent(entry, nameof(IAuditable.UpdatedAt), now);
                    SetCurrent(entry, nameof(IAuditable.UpdatedByUserId), modUser);
                    entry.Property(nameof(IAuditable.CreatedAt)).IsModified = false;
                    entry.Property(nameof(IAuditable.CreatedByUserId)).IsModified = false;
                    break;
            }
        }
    }

    private string RequireUserId(EntityEntry entry)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            throw new InvalidOperationException(
                $"Cannot save {entry.Entity.GetType().Name}: ICurrentUserService.UserId is null. " +
                "Audited writes require an authenticated user (CLAUDE.md §2 #8).");
        }

        return userId;
    }

    private static void SetCurrent(EntityEntry entry, string propertyName, object value)
        => entry.Property(propertyName).CurrentValue = value;
}
