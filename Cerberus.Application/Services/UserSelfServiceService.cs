using Cerberus.Application.DTOs;
using Cerberus.Application.Exceptions;
using Cerberus.Application.Common;
using Cerberus.Application.Interfaces.Repositories;
using Cerberus.Application.Interfaces.Security;
using Cerberus.Application.Interfaces.Services;
using Cerberus.Domain.Auditing;
using Cerberus.Domain.Consents;
using Cerberus.Domain.Exceptions;
using Cerberus.Domain.Organizations;
using Cerberus.Domain.Users;

namespace Cerberus.Application.Services;

/// <summary>Operations a signed-in user performs on his own account: consents and invitations.</summary>
public interface IUserSelfServiceService
{
    Task<IReadOnlyList<ConsentDto>> ListConsentsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task RevokeConsentAsync(Guid userId, Guid consentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserMembershipDto>> ListMembershipsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<InvitationInfoDto> GetInvitationAsync(string token, CancellationToken cancellationToken = default);
    Task AcceptInvitationAsync(Guid userId, string token, CancellationToken cancellationToken = default);
    Task<OperationResult> RegisterFromInvitationAsync(RegisterFromInvitationDto dto, CancellationToken cancellationToken = default);
}

public class UserSelfServiceService(
    IUserConsentRepository consentRepository,
    IOidcClientRepository clientRepository,
    IOrganizationRepository organizationRepository,
    IMembershipRepository membershipRepository,
    IInvitationRepository invitationRepository,
    IUserRepository userRepository,
    IIdentityService identityService,
    ITokenRevocationService tokenRevocation,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    IClock clock) : IUserSelfServiceService
{
    public async Task<IReadOnlyList<ConsentDto>> ListConsentsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var consents = await consentRepository.ListActiveForUserAsync(userId, clock.UtcNow, cancellationToken);
        var clients = await clientRepository.ListAsync(cancellationToken);
        var organizations = await organizationRepository.GetByIdsAsync(consents.Select(c => c.OrganizationId), cancellationToken);
        return consents.Select(c => new ConsentDto(
            c.Id,
            clients.FirstOrDefault(x => x.Id == c.OidcClientId)?.DisplayName ?? "?",
            organizations.FirstOrDefault(o => o.Id == c.OrganizationId)?.Name ?? "?",
            c.GrantedScopes,
            c.GrantedAt,
            c.ExpiresAt)).ToList();
    }

    public async Task RevokeConsentAsync(Guid userId, Guid consentId, CancellationToken cancellationToken = default)
    {
        var consent = await consentRepository.GetForUserAsync(userId, consentId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(UserConsent), consentId);
        var authorizationId = consent.AuthorizationId;
        var client = await clientRepository.GetByIdAsync(consent.OidcClientId, cancellationToken);

        consent.Revoke(clock.UtcNow);
        audit.Write(AuditActions.ConsentRevoked, AuditOutcome.Success, consent.OrganizationId, nameof(UserConsent), consent.Id, new { client = client?.ClientId });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (authorizationId is not null)
        {
            await tokenRevocation.RevokeAuthorizationAsync(authorizationId, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<UserMembershipDto>> ListMembershipsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var memberships = await membershipRepository.ListByUserAsync(userId, cancellationToken);
        var organizations = await organizationRepository.GetByIdsAsync(memberships.Select(m => m.OrganizationId), cancellationToken);
        return memberships
            .Where(m => m.Status != MembershipStatus.Revoked)
            .Join(organizations, m => m.OrganizationId, o => o.Id, (m, o) => new UserMembershipDto(o.Id, o.Name, o.Slug, m.Status))
            .ToList();
    }

    public async Task<InvitationInfoDto> GetInvitationAsync(string token, CancellationToken cancellationToken = default)
    {
        var invitation = await FindInvitationAsync(token, cancellationToken);
        if (invitation is null)
        {
            return new InvitationInfoDto(string.Empty, string.Empty, false, false);
        }

        var organization = await organizationRepository.GetByIdAsync(invitation.OrganizationId, cancellationToken);
        var account = await userRepository.FindByNormalizedEmailAsync(invitation.NormalizedEmail, cancellationToken);
        var valid = invitation.IsPending(clock.UtcNow) && organization is { IsActive: true };
        return new InvitationInfoDto(organization?.Name ?? string.Empty, invitation.Email, valid, account is not null);
    }

    public async Task AcceptInvitationAsync(Guid userId, string token, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken) ?? throw new EntityNotFoundException(nameof(User), userId);
        var invitation = await GetValidInvitationAsync(token, cancellationToken);
        Accept(invitation, user, await membershipRepository.GetAsync(invitation.OrganizationId, user.Id, cancellationToken));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<OperationResult> RegisterFromInvitationAsync(RegisterFromInvitationDto dto, CancellationToken cancellationToken = default)
    {
        var invitation = await GetValidInvitationAsync(dto.Token, cancellationToken);
        if (await userRepository.FindByNormalizedEmailAsync(invitation.NormalizedEmail, cancellationToken) is not null)
        {
            return OperationResult.Failure("An account already exists for this email address. Sign in to accept the invitation.");
        }

        var now = clock.UtcNow;
        var user = User.Create(dto.FirstName, dto.LastName, invitation.Email, dto.UserName, now);
        if (await userRepository.FindByNormalizedUserNameAsync(user.NormalizedUserName, cancellationToken) is not null)
        {
            return OperationResult.Failure("This login is already taken.");
        }

        // Receiving the invitation link proves ownership of the email address.
        user.ConfirmEmail(now);

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await identityService.CreateUserAsync(user, dto.Password, cancellationToken);
            if (!result.Succeeded)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return result;
            }

            audit.Write(AuditActions.UserCreated, AuditOutcome.Success, invitation.OrganizationId, nameof(User), user.Id, new { source = "invitation" }, user.Id);
            Accept(invitation, user, null);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            return OperationResult.Success;
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    private void Accept(Invitation invitation, User user, OrganizationMembership? membership)
    {
        var now = clock.UtcNow;
        invitation.Accept(user, now);
        if (membership is null)
        {
            membershipRepository.Add(OrganizationMembership.Create(invitation.OrganizationId, user.Id, now));
        }
        else if (membership.Status == MembershipStatus.Revoked)
        {
            membership.Reactivate(now);
        }

        audit.Write(AuditActions.InvitationAccepted, AuditOutcome.Success, invitation.OrganizationId, nameof(Invitation), invitation.Id, actorUserId: user.Id);
    }

    private async Task<Invitation> GetValidInvitationAsync(string token, CancellationToken cancellationToken)
    {
        var invitation = await FindInvitationAsync(token, cancellationToken);
        var organization = invitation is null ? null : await organizationRepository.GetByIdAsync(invitation.OrganizationId, cancellationToken);
        if (invitation is null || !invitation.IsPending(clock.UtcNow) || organization is not { IsActive: true })
        {
            throw new InvalidDomainOperationException("The invitation is invalid or has expired.");
        }

        return invitation;
    }

    private Task<Invitation?> FindInvitationAsync(string token, CancellationToken cancellationToken)
        => string.IsNullOrWhiteSpace(token)
            ? Task.FromResult<Invitation?>(null)
            : invitationRepository.GetByTokenHashAsync(SecureTokens.Hash(token), cancellationToken);
}
