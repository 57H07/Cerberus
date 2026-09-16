using Cerberus.Application.Interfaces.Security;
using Cerberus.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace Cerberus.Infrastructure.Identity;

public sealed class IdentityService(UserManager<User> userManager, IPasswordHasher<User> passwordHasher) : IIdentityService
{
    private static readonly User DummyUser = User.Create("Dummy", "User", "dummy@invalid.local", "dummy", DateTime.UnixEpoch);
    private static string? _dummyHash;

    public async Task<OperationResult> CreateUserAsync(User user, string password, CancellationToken cancellationToken = default)
        => ToResult(await userManager.CreateAsync(user, password));

    public async Task<PasswordCheckResult> CheckPasswordAsync(User user, string password, CancellationToken cancellationToken = default)
    {
        if (await userManager.IsLockedOutAsync(user))
        {
            return PasswordCheckResult.LockedOut;
        }

        if (await userManager.CheckPasswordAsync(user, password))
        {
            await userManager.ResetAccessFailedCountAsync(user);
            return PasswordCheckResult.Success;
        }

        await userManager.AccessFailedAsync(user);
        return await userManager.IsLockedOutAsync(user) ? PasswordCheckResult.LockedOut : PasswordCheckResult.InvalidPassword;
    }

    public void SimulatePasswordCheck(string password)
    {
        _dummyHash ??= passwordHasher.HashPassword(DummyUser, Guid.NewGuid().ToString());
        passwordHasher.VerifyHashedPassword(DummyUser, _dummyHash, password ?? string.Empty);
    }

    public async Task<OperationResult> ValidatePasswordAsync(User user, string password, CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        foreach (var validator in userManager.PasswordValidators)
        {
            var result = await validator.ValidateAsync(userManager, user, password);
            errors.AddRange(result.Errors.Select(e => e.Description));
        }

        return errors.Count == 0 ? OperationResult.Success : new OperationResult(false, errors);
    }

    public async Task<OperationResult> ChangePasswordAsync(User user, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
        => ToResult(await userManager.ChangePasswordAsync(user, currentPassword, newPassword));

    public Task<string> GeneratePasswordResetTokenAsync(User user, CancellationToken cancellationToken = default)
        => userManager.GeneratePasswordResetTokenAsync(user);

    public async Task<OperationResult> ResetPasswordAsync(User user, string token, string newPassword, CancellationToken cancellationToken = default)
        => ToResult(await userManager.ResetPasswordAsync(user, token, newPassword));

    public Task<string> GenerateEmailConfirmationTokenAsync(User user, CancellationToken cancellationToken = default)
        => userManager.GenerateEmailConfirmationTokenAsync(user);

    public async Task<OperationResult> ConfirmEmailAsync(User user, string token, CancellationToken cancellationToken = default)
        => ToResult(await userManager.ConfirmEmailAsync(user, token));

    private static OperationResult ToResult(IdentityResult result)
        => result.Succeeded ? OperationResult.Success : new OperationResult(false, result.Errors.Select(e => e.Description).ToList());
}
