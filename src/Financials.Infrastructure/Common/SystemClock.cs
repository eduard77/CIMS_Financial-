using System.Diagnostics.CodeAnalysis;
using Financials.Application.Common;

namespace Financials.Infrastructure.Common;

internal sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
