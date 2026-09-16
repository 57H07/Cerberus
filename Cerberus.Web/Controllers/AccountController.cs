using System.Security.Claims;
using Cerberus.Application.DTOs;
using Cerberus.Application.Interfaces.Services;
using Cerberus.Application.Services;
using Cerberus.Web.Extensions;
using Cerberus.Web.Infrastructure;
using Cerberus.Web.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Cerberus.Web.Controllers;

public class AccountController(IUserAuthenticationService authenticationService, IUserSelfServiceService selfService) : Controller
{
    private const string GenericLoginError = "Invalid login or password.";

    [HttpGet, AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
        => View(new LoginViewModel { ReturnUrl = SafeReturnUrl(returnUrl) });

    [HttpPost, AllowAnonymous, EnableRateLimiting(RateLimitPolicies.Authentication)]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        model.ReturnUrl = SafeReturnUrl(model.ReturnUrl);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await authenticationService.LoginAsync(new LoginRequestDto(model.Login, model.Password), cancellationToken);
        switch (result.Status)
        {
            case LoginStatus.Succeeded:
                break;
            case LoginStatus.LockedOut:
                ModelState.AddModelError(string.Empty, "Too many failed attempts. Try again later.");
                return View(model);
            case LoginStatus.AccountDisabled:
                ModelState.AddModelError(string.Empty, "This account is disabled.");
                return View(model);
            default:
                ModelState.AddModelError(string.Empty, GenericLoginError);
                return View(model);
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(WebClaimTypes.UserId, result.UserId!.Value.ToString()),
                new Claim(WebClaimTypes.SessionId, result.SessionId!.Value.ToString()),
                new Claim(WebClaimTypes.SecurityStamp, result.SecurityStamp!),
                new Claim(WebClaimTypes.Name, model.Login.Trim())
            ],
            CookieAuthenticationDefaults.AuthenticationScheme,
            WebClaimTypes.Name,
            null);

        // Prevents session fixation: any previous cookie is replaced by a new one bound to a new server session.
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = false, IssuedUtc = DateTimeOffset.UtcNow });

        return LocalRedirect(model.ReturnUrl ?? "/");
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await SignOutCurrentSessionAsync(cancellationToken);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet, AllowAnonymous]
    public IActionResult AccessDenied() => View();

    [HttpGet, AllowAnonymous]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    [HttpPost, AllowAnonymous, EnableRateLimiting(RateLimitPolicies.Authentication)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        await authenticationService.RequestPasswordResetAsync(model.Email, cancellationToken);
        return View("ForgotPasswordConfirmation");
    }

    [HttpGet, AllowAnonymous]
    public IActionResult ResetPassword(Guid userId, string token)
        => View(new ResetPasswordViewModel { UserId = userId, Token = token });

    [HttpPost, AllowAnonymous, EnableRateLimiting(RateLimitPolicies.Authentication)]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await authenticationService.ResetPasswordAsync(model.UserId, model.Token, model.Password, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddErrors(result.Errors);
            return View(model);
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        this.NotifySuccess("Your password has been changed. All your sessions were closed.");
        return RedirectToAction(nameof(Login));
    }

    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> ConfirmEmail(Guid userId, string token, CancellationToken cancellationToken)
    {
        var result = await authenticationService.ConfirmEmailAsync(userId, token, cancellationToken);
        ViewData["Succeeded"] = result.Succeeded;
        return View();
    }

    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> Invitation(string token, CancellationToken cancellationToken)
    {
        var info = await selfService.GetInvitationAsync(token, cancellationToken);
        return View(new InvitationViewModel(token, info, User.GetUserId() is not null));
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> AcceptInvitation(string token, CancellationToken cancellationToken)
    {
        await selfService.AcceptInvitationAsync(User.RequireUserId(), token, cancellationToken);
        this.NotifySuccess("Invitation accepted.");
        return RedirectToAction("Index", "Home");
    }

    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> Register(string token, CancellationToken cancellationToken)
    {
        var info = await selfService.GetInvitationAsync(token, cancellationToken);
        if (!info.IsValid || info.AccountExists)
        {
            return RedirectToAction(nameof(Invitation), new { token });
        }

        return View(new RegisterFromInvitationViewModel { Token = token, Email = info.Email, OrganizationName = info.OrganizationName });
    }

    [HttpPost, AllowAnonymous, EnableRateLimiting(RateLimitPolicies.Authentication)]
    public async Task<IActionResult> Register(RegisterFromInvitationViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await selfService.RegisterFromInvitationAsync(
            new RegisterFromInvitationDto(model.Token, model.FirstName, model.LastName, model.UserName, model.Password), cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddErrors(result.Errors);
            return View(model);
        }

        this.NotifySuccess("Your account has been created. You can now sign in.");
        return RedirectToAction(nameof(Login));
    }

    [HttpGet, Authorize]
    public async Task<IActionResult> Manage(CancellationToken cancellationToken)
    {
        var userId = User.RequireUserId();
        var profile = await authenticationService.GetProfileAsync(userId, cancellationToken);
        if (profile is null)
        {
            return Challenge();
        }

        return View(new MyAccountViewModel(
            profile,
            await authenticationService.ListSessionsAsync(userId, cancellationToken),
            User.GetSessionId(),
            await selfService.ListConsentsAsync(userId, cancellationToken),
            await selfService.ListMembershipsAsync(userId, cancellationToken)));
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> RevokeSession(Guid sessionId, CancellationToken cancellationToken)
    {
        if (sessionId == User.GetSessionId())
        {
            await SignOutCurrentSessionAsync(cancellationToken);
            return RedirectToAction(nameof(Login));
        }

        await authenticationService.RevokeSessionAsync(User.RequireUserId(), sessionId, cancellationToken);
        this.NotifySuccess("Session revoked.");
        return RedirectToAction(nameof(Manage));
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> RevokeConsent(Guid consentId, CancellationToken cancellationToken)
    {
        await selfService.RevokeConsentAsync(User.RequireUserId(), consentId, cancellationToken);
        this.NotifySuccess("Consent revoked. The application's tokens were revoked.");
        return RedirectToAction(nameof(Manage));
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> ResendConfirmation(CancellationToken cancellationToken)
    {
        await authenticationService.SendEmailConfirmationAsync(User.RequireUserId(), cancellationToken);
        this.NotifySuccess("A confirmation email has been sent.");
        return RedirectToAction(nameof(Manage));
    }

    internal async Task SignOutCurrentSessionAsync(CancellationToken cancellationToken)
    {
        if (User.GetUserId() is { } userId && User.GetSessionId() is { } sessionId)
        {
            await authenticationService.LogoutAsync(userId, sessionId, cancellationToken);
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    private string? SafeReturnUrl(string? returnUrl) => Url.IsLocalUrl(returnUrl) ? returnUrl : null;
}
