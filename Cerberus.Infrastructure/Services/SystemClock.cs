using Cerberus.Application.Interfaces.Services;

namespace Cerberus.Infrastructure.Services;

public sealed class SystemClock(TimeProvider timeProvider) : IClock
{
    public DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
}
