namespace Cerberus.Application.DTOs;

public sealed record LoginRequestDto(string Login, string Password);

public enum LoginStatus
{
    Succeeded = 0,

    /// <summary>Unknown login or wrong password. Deliberately indistinguishable.</summary>
    InvalidCredentials = 1,

    LockedOut = 2,

    /// <summary>Only returned once the password has been verified, so it does not reveal account existence.</summary>
    AccountDisabled = 3
}

public sealed record LoginResultDto(LoginStatus Status, Guid? UserId = null, Guid? SessionId = null, string? SecurityStamp = null)
{
    public bool Succeeded => Status == LoginStatus.Succeeded;
}

public sealed record SessionDto(Guid Id, DateTime CreatedAt, DateTime LastSeenAt, DateTime ExpiresAt, string? IpAddress, string? UserAgent);

public sealed record UserProfileDto(Guid Id, string UserName, string Email, bool EmailConfirmed, string FirstName, string LastName);
