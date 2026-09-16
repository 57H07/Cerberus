namespace Cerberus.Application.Interfaces.Services;

public interface IClock
{
    DateTime UtcNow { get; }
}

/// <summary>Who performs the current operation. Implemented by the web layer from the authenticated request.</summary>
public interface IActorContext
{
    Guid? UserId { get; }

    string? IpAddress { get; }

    string? UserAgent { get; }
}
