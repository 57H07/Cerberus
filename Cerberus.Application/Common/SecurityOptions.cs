namespace Cerberus.Application.Common;

public sealed class SessionOptions
{
    public const string SectionName = "Security:Sessions";

    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromHours(8);

    public TimeSpan AbsoluteLifetime { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Minimum delay between two sliding refreshes of a session, to limit database writes.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(1);
}

public sealed class InvitationOptions
{
    public const string SectionName = "Security:Invitations";

    public TimeSpan Lifetime { get; set; } = TimeSpan.FromDays(7);
}
