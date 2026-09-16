using System.Text.RegularExpressions;
using Cerberus.Domain.Common;
using Cerberus.Domain.Exceptions;

namespace Cerberus.Domain.Users;

/// <summary>
/// Global identity. Invariants:
/// - login and email are unique after <see cref="IdentityNormalizer"/> normalization (enforced by unique indexes);
/// - login matches <see cref="UserNamePattern"/>, email has a valid shape and at most 254 characters;
/// - any credential or status change rotates the security stamp, which invalidates cookies and sessions;
/// - a disabled account cannot authenticate.
/// </summary>
public sealed partial class User : Entity
{
    public const int NameMaxLength = 100;
    public const int EmailMaxLength = 254;
    public const int UserNameMinLength = 3;
    public const int UserNameMaxLength = 64;

    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string NormalizedEmail { get; private set; } = string.Empty;
    public bool EmailConfirmed { get; private set; }
    public string UserName { get; private set; } = string.Empty;
    public string NormalizedUserName { get; private set; } = string.Empty;
    public string? PasswordHash { get; private set; }
    public string SecurityStamp { get; private set; } = NewStamp();
    public UserStatus Status { get; private set; }
    public int AccessFailedCount { get; private set; }
    public DateTimeOffset? LockoutEnd { get; private set; }

    public bool IsActive => Status == UserStatus.Active;

    private User()
    {
    }

    private User(DateTime utcNow) : base(utcNow)
    {
    }

    public static User Create(string firstName, string lastName, string email, string userName, DateTime utcNow)
    {
        var user = new User(utcNow)
        {
            FirstName = Check.Required(firstName, nameof(FirstName), NameMaxLength),
            LastName = Check.Required(lastName, nameof(LastName), NameMaxLength),
            Status = UserStatus.Active
        };
        user.ApplyEmail(email);
        user.ApplyUserName(userName);
        return user;
    }

    public void UpdateProfile(string firstName, string lastName, DateTime utcNow)
    {
        FirstName = Check.Required(firstName, nameof(FirstName), NameMaxLength);
        LastName = Check.Required(lastName, nameof(LastName), NameMaxLength);
        Touch(utcNow);
    }

    public void ChangeEmail(string email, DateTime utcNow)
    {
        var previous = NormalizedEmail;
        ApplyEmail(email);
        if (previous != NormalizedEmail)
        {
            EmailConfirmed = false;
            RotateSecurityStamp();
        }

        Touch(utcNow);
    }

    public void ChangeUserName(string userName, DateTime utcNow)
    {
        var previous = NormalizedUserName;
        ApplyUserName(userName);
        if (previous != NormalizedUserName)
        {
            RotateSecurityStamp();
        }

        Touch(utcNow);
    }

    public void ConfirmEmail(DateTime utcNow)
    {
        EmailConfirmed = true;
        Touch(utcNow);
    }

    public void SetPasswordHash(string? passwordHash, DateTime utcNow)
    {
        PasswordHash = passwordHash;
        Touch(utcNow);
    }

    public void SetSecurityStamp(string stamp)
    {
        SecurityStamp = Check.Required(stamp, nameof(SecurityStamp), 128);
    }

    public void RotateSecurityStamp() => SecurityStamp = NewStamp();

    public void Disable(DateTime utcNow)
    {
        if (Status == UserStatus.Disabled)
        {
            return;
        }

        Status = UserStatus.Disabled;
        RotateSecurityStamp();
        Touch(utcNow);
    }

    public void Enable(DateTime utcNow)
    {
        if (Status == UserStatus.Active)
        {
            return;
        }

        Status = UserStatus.Active;
        AccessFailedCount = 0;
        LockoutEnd = null;
        Touch(utcNow);
    }

    public bool IsLockedOut(DateTimeOffset utcNow) => LockoutEnd.HasValue && LockoutEnd.Value > utcNow;

    public int RecordFailedAccess() => ++AccessFailedCount;

    public void ResetFailedAccess() => AccessFailedCount = 0;

    public void SetLockoutEnd(DateTimeOffset? lockoutEnd) => LockoutEnd = lockoutEnd;

    public static bool IsValidEmail(string? email)
    {
        return !string.IsNullOrWhiteSpace(email)
            && email.Trim().Length <= EmailMaxLength
            && EmailPattern().IsMatch(email.Trim());
    }

    public static bool IsValidUserName(string? userName)
    {
        return !string.IsNullOrWhiteSpace(userName) && UserNamePattern().IsMatch(userName.Trim());
    }

    private void ApplyEmail(string email)
    {
        if (!IsValidEmail(email))
        {
            throw new DomainValidationException("Email address is invalid.", nameof(Email));
        }

        Email = email.Trim();
        NormalizedEmail = IdentityNormalizer.Normalize(Email);
    }

    private void ApplyUserName(string userName)
    {
        if (!IsValidUserName(userName))
        {
            throw new DomainValidationException(
                $"Login must be {UserNameMinLength}-{UserNameMaxLength} characters: letters, digits, '.', '_' or '-'.",
                nameof(UserName));
        }

        UserName = userName.Trim();
        NormalizedUserName = IdentityNormalizer.Normalize(UserName);
    }

    private static string NewStamp() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(20));

    [GeneratedRegex(@"^[a-zA-Z0-9._-]{3,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex UserNamePattern();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}
