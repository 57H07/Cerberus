using Cerberus.Application.Interfaces.Services;
using Cerberus.Domain.Common;
using Cerberus.Domain.Users;
using Cerberus.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Cerberus.Infrastructure.Identity;

/// <summary>
/// ASP.NET Core Identity store backed by the domain <see cref="User"/> entity, so the domain stays free of
/// Identity base classes while hashing, lockout, security stamps and token providers are reused.
/// Normalized values are always computed by the domain (<see cref="IdentityNormalizer"/>).
/// </summary>
public sealed class UserStore(CerberusDbContext context, IClock clock) :
    IUserPasswordStore<User>,
    IUserEmailStore<User>,
    IUserLockoutStore<User>,
    IUserSecurityStampStore<User>
{
    public async Task<IdentityResult> CreateAsync(User user, CancellationToken cancellationToken)
    {
        context.Users.Add(user);
        await context.SaveChangesAsync(cancellationToken);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> UpdateAsync(User user, CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return IdentityResult.Success;
        }
        catch (DbUpdateConcurrencyException)
        {
            return IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure());
        }
    }

    public async Task<IdentityResult> DeleteAsync(User user, CancellationToken cancellationToken)
    {
        context.Users.Remove(user);
        await context.SaveChangesAsync(cancellationToken);
        return IdentityResult.Success;
    }

    public Task<User?> FindByIdAsync(string userId, CancellationToken cancellationToken)
        => Guid.TryParse(userId, out var id)
            ? context.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            : Task.FromResult<User?>(null);

    public Task<User?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
        => context.Users.FirstOrDefaultAsync(u => u.NormalizedUserName == normalizedUserName, cancellationToken);

    public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
        => context.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

    public Task<string> GetUserIdAsync(User user, CancellationToken cancellationToken) => Task.FromResult(user.Id.ToString());

    public Task<string?> GetUserNameAsync(User user, CancellationToken cancellationToken) => Task.FromResult<string?>(user.UserName);

    public Task SetUserNameAsync(User user, string? userName, CancellationToken cancellationToken)
    {
        user.ChangeUserName(userName ?? string.Empty, clock.UtcNow);
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(User user, CancellationToken cancellationToken) => Task.FromResult<string?>(user.NormalizedUserName);

    public Task SetNormalizedUserNameAsync(User user, string? normalizedName, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SetPasswordHashAsync(User user, string? passwordHash, CancellationToken cancellationToken)
    {
        user.SetPasswordHash(passwordHash, clock.UtcNow);
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(User user, CancellationToken cancellationToken) => Task.FromResult(user.PasswordHash);

    public Task<bool> HasPasswordAsync(User user, CancellationToken cancellationToken) => Task.FromResult(user.PasswordHash is not null);

    public Task SetEmailAsync(User user, string? email, CancellationToken cancellationToken)
    {
        user.ChangeEmail(email ?? string.Empty, clock.UtcNow);
        return Task.CompletedTask;
    }

    public Task<string?> GetEmailAsync(User user, CancellationToken cancellationToken) => Task.FromResult<string?>(user.Email);

    public Task<bool> GetEmailConfirmedAsync(User user, CancellationToken cancellationToken) => Task.FromResult(user.EmailConfirmed);

    public Task SetEmailConfirmedAsync(User user, bool confirmed, CancellationToken cancellationToken)
    {
        if (confirmed)
        {
            user.ConfirmEmail(clock.UtcNow);
        }

        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedEmailAsync(User user, CancellationToken cancellationToken) => Task.FromResult<string?>(user.NormalizedEmail);

    public Task SetNormalizedEmailAsync(User user, string? normalizedEmail, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<DateTimeOffset?> GetLockoutEndDateAsync(User user, CancellationToken cancellationToken) => Task.FromResult(user.LockoutEnd);

    public Task SetLockoutEndDateAsync(User user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
    {
        user.SetLockoutEnd(lockoutEnd);
        return Task.CompletedTask;
    }

    public Task<int> IncrementAccessFailedCountAsync(User user, CancellationToken cancellationToken) => Task.FromResult(user.RecordFailedAccess());

    public Task ResetAccessFailedCountAsync(User user, CancellationToken cancellationToken)
    {
        user.ResetFailedAccess();
        return Task.CompletedTask;
    }

    public Task<int> GetAccessFailedCountAsync(User user, CancellationToken cancellationToken) => Task.FromResult(user.AccessFailedCount);

    public Task<bool> GetLockoutEnabledAsync(User user, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task SetLockoutEnabledAsync(User user, bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SetSecurityStampAsync(User user, string stamp, CancellationToken cancellationToken)
    {
        user.SetSecurityStamp(stamp);
        return Task.CompletedTask;
    }

    public Task<string?> GetSecurityStampAsync(User user, CancellationToken cancellationToken) => Task.FromResult<string?>(user.SecurityStamp);

    public void Dispose()
    {
    }
}

public sealed class DomainLookupNormalizer : ILookupNormalizer
{
    public string? NormalizeName(string? name) => name is null ? null : IdentityNormalizer.Normalize(name);

    public string? NormalizeEmail(string? email) => email is null ? null : IdentityNormalizer.Normalize(email);
}
