using Cerberus.Application.Interfaces.Repositories;
using Cerberus.Application.Interfaces.Services;
using Cerberus.Application.Services;
using Cerberus.Domain.Applications;
using Cerberus.Domain.Auditing;
using Cerberus.Domain.Clients;
using Cerberus.Domain.Consents;
using Cerberus.Domain.Organizations;
using Cerberus.Domain.Users;

namespace Cerberus.Application.Oidc;

public interface IOidcAuthorizationService
{
    /// <summary>Validates an authorization request for an authenticated user and decides the next step.</summary>
    Task<AuthorizationEvaluationDto> EvaluateAsync(AuthorizationRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Records the user's consent for the validated organization, then returns the token subject.</summary>
    Task<AuthorizationEvaluationDto> GrantConsentAsync(AuthorizationRequestDto request, CancellationToken cancellationToken = default);

    Task RecordConsentDeniedAsync(AuthorizationRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Links the permanent library authorization created for a remembered consent.</summary>
    Task LinkAuthorizationAsync(Guid userId, Guid oidcClientId, Guid organizationId, string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-validates the whole context at the token endpoint (code redemption and refresh): user, client, application,
    /// organization, membership and consent must still be valid. Claims are rebuilt from current data.
    /// </summary>
    Task<AuthorizationEvaluationDto> RevalidateAsync(Guid userId, string clientId, Guid organizationId, IReadOnlyList<string> scopes,
        Guid? sessionId, CancellationToken cancellationToken = default);
}

public class OidcAuthorizationService(
    IUserRepository userRepository,
    IOrganizationRepository organizationRepository,
    IMembershipRepository membershipRepository,
    IOidcClientRepository clientRepository,
    IClientApplicationRepository applicationRepository,
    IApiScopeRepository apiScopeRepository,
    IRoleAssignmentRepository roleAssignmentRepository,
    IUserConsentRepository consentRepository,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    IClock clock) : IOidcAuthorizationService
{
    private const string OrganizationUnavailable = "The requested organization is not available for this user and application.";

    public async Task<AuthorizationEvaluationDto> EvaluateAsync(AuthorizationRequestDto request, CancellationToken cancellationToken = default)
    {
        var context = await LoadContextAsync(request.UserId, request.ClientId, request.Scopes, cancellationToken);
        if (context.Rejection is not null)
        {
            return context.Rejection;
        }

        Organization organization;
        if (string.IsNullOrWhiteSpace(request.Organization))
        {
            var eligible = await ListEligibleOrganizationsAsync(context.User!, context.Client!, context.Application!, cancellationToken);
            if (eligible.Count == 0)
            {
                await AuditTenantRejectionAsync(request, null, "no_eligible_organization", cancellationToken);
                return AuthorizationEvaluationDto.Reject(OidcErrors.AccessDenied, OrganizationUnavailable);
            }

            if (eligible.Count > 1)
            {
                return new AuthorizationEvaluationDto(
                    AuthorizationOutcome.OrganizationSelectionRequired,
                    OrganizationOptions: eligible.Select(o => new OrganizationOptionDto(o.Id, o.Slug, o.Name)).ToList());
            }

            organization = eligible[0];
        }
        else
        {
            var requested = await ResolveOrganizationAsync(request.Organization, cancellationToken);
            var membership = requested is null ? null : await membershipRepository.GetAsync(requested.Id, request.UserId, cancellationToken);
            var failure = TenantAccessRules.Evaluate(context.User!, requested, membership, context.Client!, context.Application!);
            if (failure != TenantAccessFailure.None)
            {
                await AuditTenantRejectionAsync(request, requested?.Id, failure.ToString(), cancellationToken);
                return AuthorizationEvaluationDto.Reject(OidcErrors.AccessDenied, OrganizationUnavailable);
            }

            organization = requested!;
        }

        var consent = await consentRepository.GetAsync(request.UserId, context.Client!.Id, organization.Id, cancellationToken);
        var decision = ConsentEvaluator.Evaluate(context.Client, consent, request.Scopes, request.PromptConsent, clock.UtcNow);
        if (decision == ConsentDecision.Required)
        {
            return new AuthorizationEvaluationDto(
                AuthorizationOutcome.ConsentRequired,
                Consent: new ConsentPromptDto(
                    context.Client.DisplayName,
                    new OrganizationOptionDto(organization.Id, organization.Slug, organization.Name),
                    DescribeScopes(request.Scopes, context.ApiScopes)));
        }

        var authorizationId = context.Client.ConsentPolicy == ConsentPolicy.Remembered ? consent?.AuthorizationId : null;
        return await GrantAsync(context, organization, request.Scopes, request.SessionId, authorizationId, cancellationToken);
    }

    public async Task<AuthorizationEvaluationDto> GrantConsentAsync(AuthorizationRequestDto request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Organization))
        {
            return AuthorizationEvaluationDto.Reject(OidcErrors.InvalidRequest, "The organization is required.");
        }

        var context = await LoadContextAsync(request.UserId, request.ClientId, request.Scopes, cancellationToken);
        if (context.Rejection is not null)
        {
            return context.Rejection;
        }

        // The organization posted by the consent form is re-validated: it is never trusted as is.
        var organization = await ResolveOrganizationAsync(request.Organization, cancellationToken);
        var membership = organization is null ? null : await membershipRepository.GetAsync(organization.Id, request.UserId, cancellationToken);
        var failure = TenantAccessRules.Evaluate(context.User!, organization, membership, context.Client!, context.Application!);
        if (failure != TenantAccessFailure.None)
        {
            await AuditTenantRejectionAsync(request, organization?.Id, failure.ToString(), cancellationToken);
            return AuthorizationEvaluationDto.Reject(OidcErrors.AccessDenied, OrganizationUnavailable);
        }

        var now = clock.UtcNow;
        var client = context.Client!;
        var consent = await consentRepository.GetAsync(request.UserId, client.Id, organization!.Id, cancellationToken);
        if (consent is null)
        {
            consent = UserConsent.Grant(request.UserId, client.Id, organization.Id, request.Scopes, client.ConsentLifetime, now);
            consentRepository.Add(consent);
        }
        else
        {
            consent.Renew(request.Scopes, client.ConsentLifetime, now);
        }

        audit.Write(AuditActions.ConsentGranted, organizationId: organization.Id, targetType: nameof(OidcClient), targetId: client.ClientId,
            details: new { scopes = request.Scopes });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var authorizationId = client.ConsentPolicy == ConsentPolicy.Remembered ? consent.AuthorizationId : null;
        return await GrantAsync(context, organization, request.Scopes, request.SessionId, authorizationId, cancellationToken);
    }

    public async Task RecordConsentDeniedAsync(AuthorizationRequestDto request, CancellationToken cancellationToken = default)
    {
        audit.Write(AuditActions.AuthorizationDenied, AuditOutcome.Denied, targetType: nameof(OidcClient), targetId: request.ClientId,
            details: new { reason = "consent_denied" });
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task LinkAuthorizationAsync(Guid userId, Guid oidcClientId, Guid organizationId, string authorizationId, CancellationToken cancellationToken = default)
    {
        var consent = await consentRepository.GetAsync(userId, oidcClientId, organizationId, cancellationToken);
        if (consent is null || !consent.IsActive(clock.UtcNow))
        {
            return;
        }

        consent.LinkAuthorization(authorizationId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<AuthorizationEvaluationDto> RevalidateAsync(Guid userId, string clientId, Guid organizationId, IReadOnlyList<string> scopes,
        Guid? sessionId, CancellationToken cancellationToken = default)
    {
        var context = await LoadContextAsync(userId, clientId, scopes, cancellationToken);
        if (context.Rejection is not null)
        {
            return AuthorizationEvaluationDto.Reject(OidcErrors.InvalidGrant, "The authorization is no longer valid.");
        }

        var organization = await organizationRepository.GetByIdAsync(organizationId, cancellationToken);
        var membership = organization is null ? null : await membershipRepository.GetAsync(organization.Id, userId, cancellationToken);
        if (TenantAccessRules.Evaluate(context.User!, organization, membership, context.Client!, context.Application!) != TenantAccessFailure.None)
        {
            return AuthorizationEvaluationDto.Reject(OidcErrors.InvalidGrant, "The authorization is no longer valid.");
        }

        if (context.Client!.ConsentPolicy != ConsentPolicy.Trusted)
        {
            var consent = await consentRepository.GetAsync(userId, context.Client.Id, organizationId, cancellationToken);
            if (consent is null || !consent.Covers(scopes, clock.UtcNow))
            {
                return AuthorizationEvaluationDto.Reject(OidcErrors.InvalidGrant, "The consent is no longer valid.");
            }
        }

        return await GrantAsync(context, organization!, scopes, sessionId, null, cancellationToken, audited: false);
    }

    private async Task<AuthorizationEvaluationDto> GrantAsync(RequestContext context, Organization organization, IReadOnlyList<string> scopes,
        Guid? sessionId, string? authorizationId, CancellationToken cancellationToken, bool audited = true)
    {
        var roles = scopes.Contains(Scopes.OrganizationRoles)
            ? (await roleAssignmentRepository.ListOrganizationAssignmentsAsync(organization.Id, context.User!.Id, cancellationToken)).Select(a => a.RoleName).ToList()
            : [];

        var claims = ClaimsPolicy.Build(context.User!, organization, scopes, roles, sessionId);
        var resources = context.ApiScopes.Where(s => scopes.Contains(s.Name)).Select(s => s.Resource).Distinct(StringComparer.Ordinal).ToList();

        if (audited)
        {
            audit.Write(AuditActions.TokenIssued, organizationId: organization.Id, targetType: nameof(OidcClient), targetId: context.Client!.ClientId,
                actorUserId: context.User!.Id, details: new { scopes });
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new AuthorizationEvaluationDto(
            AuthorizationOutcome.Granted,
            new TokenSubject(context.User!.Id, context.Client!.Id, organization.Id, scopes, resources, claims, authorizationId,
                context.Client.ConsentPolicy == ConsentPolicy.Remembered));
    }

    private async Task<RequestContext> LoadContextAsync(Guid userId, string clientId, IReadOnlyList<string> scopes, CancellationToken cancellationToken)
    {
        var client = await clientRepository.GetByClientIdAsync(clientId, cancellationToken);
        var application = client is null ? null : await applicationRepository.GetByIdAsync(client.ClientApplicationId, cancellationToken);
        if (client is null || application is null || !client.IsActive || !application.IsActive)
        {
            return RequestContext.Rejected(AuthorizationEvaluationDto.Reject(OidcErrors.UnauthorizedClient, "The client application is not available."));
        }

        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return RequestContext.Rejected(AuthorizationEvaluationDto.Reject(OidcErrors.AccessDenied, "The user account is not available."));
        }

        if (!scopes.Contains(Scopes.OpenId) || !client.AllowsScopes(scopes))
        {
            return RequestContext.Rejected(AuthorizationEvaluationDto.Reject(OidcErrors.InvalidScope, "The requested scopes are not allowed for this client."));
        }

        var apiScopeNames = scopes.Where(s => !Scopes.Standard.Contains(s)).ToList();
        var apiScopes = apiScopeNames.Count == 0 ? [] : await apiScopeRepository.GetByNamesAsync(apiScopeNames, cancellationToken);
        if (apiScopes.Count != apiScopeNames.Count)
        {
            return RequestContext.Rejected(AuthorizationEvaluationDto.Reject(OidcErrors.InvalidScope, "Unknown scope requested."));
        }

        return new RequestContext(user, client, application, apiScopes, null);
    }

    private async Task<List<Organization>> ListEligibleOrganizationsAsync(User user, OidcClient client, ClientApplication application, CancellationToken cancellationToken)
    {
        var memberships = await membershipRepository.ListByUserAsync(user.Id, cancellationToken);
        var organizations = await organizationRepository.GetByIdsAsync(memberships.Select(m => m.OrganizationId), cancellationToken);
        return organizations
            .Where(o => TenantAccessRules.Evaluate(user, o, memberships.FirstOrDefault(m => m.OrganizationId == o.Id), client, application) == TenantAccessFailure.None)
            .ToList();
    }

    private Task<Organization?> ResolveOrganizationAsync(string value, CancellationToken cancellationToken)
    {
        var trimmed = value.Trim();
        if (Guid.TryParse(trimmed, out var id))
        {
            return organizationRepository.GetByIdAsync(id, cancellationToken);
        }

        return Organization.IsValidSlug(trimmed)
            ? organizationRepository.GetBySlugAsync(trimmed, cancellationToken)
            : Task.FromResult<Organization?>(null);
    }

    private async Task AuditTenantRejectionAsync(AuthorizationRequestDto request, Guid? organizationId, string reason, CancellationToken cancellationToken)
    {
        audit.Write(AuditActions.TenantRejected, AuditOutcome.Denied, organizationId, nameof(OidcClient), request.ClientId,
            new { reason, requested = request.Organization }, request.UserId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static List<ScopeDescriptionDto> DescribeScopes(IEnumerable<string> scopes, IReadOnlyList<ApiScope> apiScopes)
    {
        return scopes.Select(scope => scope switch
        {
            Scopes.OpenId => new ScopeDescriptionDto(scope, "Your identifier", "Know who you are."),
            Scopes.Profile => new ScopeDescriptionDto(scope, "Your profile", "First name, last name and login."),
            Scopes.Email => new ScopeDescriptionDto(scope, "Your email address", null),
            Scopes.OfflineAccess => new ScopeDescriptionDto(scope, "Offline access", "Keep access while you are not using the application."),
            Scopes.OrganizationRoles => new ScopeDescriptionDto(scope, "Your roles in the organization", null),
            _ => apiScopes.Where(s => s.Name == scope).Select(s => new ScopeDescriptionDto(s.Name, s.DisplayName, s.Description)).First()
        }).ToList();
    }

    private sealed record RequestContext(User? User, OidcClient? Client, ClientApplication? Application, IReadOnlyList<ApiScope> ApiScopes,
        AuthorizationEvaluationDto? Rejection)
    {
        public static RequestContext Rejected(AuthorizationEvaluationDto rejection) => new(null, null, null, [], rejection);
    }
}
