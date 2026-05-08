using System.Diagnostics.CodeAnalysis;
using Financials.Application.Common;

namespace Financials.Infrastructure.Common;

[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Resolved by the DI container via AddSingleton<IClock, SystemClock>().")]
internal sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
