# 06 - Politique de sécurité et plan de tests

## 1. Politique de sécurité

### Authentification
- **Hachage** : ASP.NET Core Identity `PasswordHasher` V3 (PBKDF2-HMAC-SHA512, 100 000 itérations, sel aléatoire) avec rehachage transparent.
- **Mots de passe** : 12 caractères minimum, 4 caractères distincts (approche NIST 800-63B : longueur plutôt que complexité).
- **Brute force** :
  - **Verrouillage** : 5 échecs, puis 15 min (Identity lockout).
  - **Rate limiting par IP** : 10 req/min sur login, mot de passe oublié, reset, inscription ; 60 req/min sur `/connect/token`.
  - **Anti-énumération** : même message pour login inconnu et mot de passe faux ; vérification factice du hash pour un login inconnu (temps constant) ; « mot de passe oublié » répond toujours la même chose.
- **Reset** : jeton Data Protection lié au security stamp, valide 2 h, usage effectif unique (le stamp change). La réussite révoque toutes les sessions et tous les tokens.
- **Confirmation d'email** : jeton Identity. L'inscription par invitation vaut confirmation.
- **Sessions** :
  - `UserSession` serveur, cookie `__Host-` Secure, HttpOnly, SameSite=Lax.
  - Protection contre la fixation : sign-out puis sign-in à la connexion.
  - Révocation individuelle depuis « My account ».
- **Comptes désactivés** : connexion refusée, sessions et tokens révoqués, refresh refusé.

### OAuth 2.0 / OIDC
- **Flux et PKCE** : code + PKCE S256 obligatoire pour tous les clients ; `state` renvoyé tel quel par OpenIddict ; `nonce` inclus dans l'ID token.
- **Redirect URIs** : égalité exacte, sans wildcard ; enregistrement HTTPS uniquement, sauf loopback.
- **Codes et tokens** : codes à usage unique (rejeu = révocation) ; issuer fixe ; audience par resource ; access tokens de 5 min.
- **Clés** : clés persistantes chiffrées, rotation, `kid`.
- **Secrets clients** :
  - générés aléatoirement (256 bits) ;
  - **hachés** par OpenIddict ;
  - affichés une seule fois, jamais journalisés ni audités ;
  - aucun secret dans le dépôt (`.env`, user-secrets, variables d'environnement).
- **Open redirect** : `ReturnUrl` local uniquement (`Url.IsLocalUrl`, `LocalRedirect`) ; `post_logout_redirect_uri` validée par OpenIddict.
- **Déconnexion** : confirmation obligatoire avec antiforgery.
- **Erreurs** : codes OIDC standards ; descriptions génériques pour les refus de tenant ; aucune stack trace hors Development ; ProblemDetails pour le JSON.

### Multi-tenancy
Voir 04 §3 et 05 §1 :
- validation serveur ;
- revalidation au refresh ;
- repositories filtrés ;
- garde d'accès ;
- anti-escalade ;
- audit des refus.

### Web
- **Protections** : antiforgery global (sauf endpoints protocolaires) ; HSTS hors Development ; `X-Frame-Options: DENY`, `frame-ancestors 'none'`, `nosniff`, `Referrer-Policy: no-referrer`.
- **CSP stricte** : pas de script inline, sauf le script d'auto-soumission `form_post` d'OpenIddict, autorisé par son empreinte SHA-256.
- **Journaux** : pas de corps d'email, de token ni de secret.

### Audit
`AuditEntry` est écrit pour :
- les connexions réussies ou échouées, le lockout, la déconnexion ;
- le reset de mot de passe, la confirmation d'email ;
- les autorisations émises, les refus de tenant, le consentement donné ou révoqué ;
- toutes les opérations d'administration (utilisateurs, organisations, membres, invitations, rôles, affectations, applications, clients, secrets, scopes) ;
- la création de clés ;
- les refus d'accès inter-tenant et les tentatives d'escalade.

Consultation : `/Admin/Audit` (plateforme) et `/OrgAdmin/{org}/Organization/Audit` (limité à l'organisation).

## 2. Plan de tests et couverture

Suites :
- **`Cerberus.Domain.Tests`** (78 tests) : invariants.
- **`Cerberus.Application.Tests`** (25) : services et politiques avec Moq.
- **`Cerberus.ArchitectureTests`** (4) : sens des dépendances.
- **`Cerberus.IntegrationTests`** (37) : IdP réel sur SQL Server Testcontainers, flux HTTP complets.

| Exigence (spec §11 étape 5) | Tests |
|---|---|
| Authentification valide / invalide | `AuthenticationTests.Login_WithValidCredentials_ShouldSignIn`, `Login_WithWrongPasswordOrUnknownUser_ShouldReturnSameGenericError`, `Login_WithEmailInDifferentCase_ShouldSignIn` |
| Verrouillage | `Login_AfterFiveFailures_ShouldLockOutEvenWithCorrectPassword` |
| Compte désactivé | `Login_DisabledAccount_ShouldBeRefused`, `DisablingUser_ShouldInvalidateSessionsAndRefreshTokens`, `TenantAccessRulesTests.Evaluate_DisabledUser_ShouldFail` |
| Utilisateur dans plusieurs organisations | `OidcFlowTests.OrganizationRolesScope_ShouldReleaseOnlyRolesOfSelectedOrganization`, `OidcAuthorizationServiceTests.Evaluate_SeveralEligibleOrganizations_ShouldAskForSelection` |
| Tenant inexistant | `OidcFlowTests.UnknownOrganization_ShouldBeDenied` |
| Tenant non autorisé | `OrganizationNotGrantedToApplication_ShouldBeDenied`, `TenantAccessRulesTests.Evaluate_AccessSuspendedByOrganization_ShouldFail` |
| Utilisateur non membre | `NonMember_ShouldBeDeniedEvenWithOrganizationId`, `ConsentForm_TamperedOrganization_ShouldBeRevalidated` |
| Isolation entre organisations | `AdministrationTests.OrganizationAdministrator_CannotReadOrModifyAnotherOrganization`, `..._CannotUseAnotherOrganizationsIdentifiersThroughOwnRoute`, `SuspendedMembership_ShouldRejectRefresh` |
| Affectation de rôles | `RoleManager_CanAssignRoleWithinHisOwnPermissions`, `RoleTests.*` |
| Escalade de privilèges | `RoleManager_CannotGrantPermissionsHeDoesNotHold`, `OrganizationAdministrator_CannotAccessPlatformAdministration`, `AccessGuardTests.*` |
| Redirect URI invalide | `OidcFlowTests.InvalidRedirectUri_ShouldNotRedirect`, `OidcClientTests.RedirectUri_Invalid_ShouldBeRejected` |
| PKCE invalide | `MissingPkce_ShouldBeRejected`, `WrongCodeVerifier_ShouldBeRejected` |
| Code réutilisé | `ReusedAuthorizationCode_ShouldBeRejectedAndRevokeIssuedTokens` |
| Token expiré | `ExpiredAccessToken_ShouldFailValidation`, `AccessToken_ForOtherAudience_ShouldFailValidation` |
| Claims incorrects ou excessifs | `FullFlow_ShouldIssueSignedTokensWithMinimalClaims`, `ClaimsPolicyTests.*` |
| Client désactivé | `PlatformAdministrator_CreatesClient_SecretShownOnce_ThenDisablingClientBlocksTokens`, `TenantAccessRulesTests.Evaluate_DisabledClient_ShouldFail` |
| Consentement révoqué | `AuthenticationTests.RevokedConsent_ShouldRevokeRefreshTokensAndPromptAgain`, `OidcAuthorizationServiceTests.Revalidate_AfterConsentRevoked_ShouldReturnInvalidGrant` |
| Sessions et déconnexion | `Logout_ShouldRevokeServerSession_EvenIfOldCookieIsReplayed`, `PasswordReset_ShouldChangePasswordAndRevokeExistingSessions` |
| Clés / JWKS | `Jwks_ShouldPublishActiveAndPendingKeysWithoutPrivateMaterial`, `SessionAndKeyTests.*` |

**Commandes** :
```bash
dotnet test Cerberus.slnx
```
Docker doit être démarré pour les tests d'intégration.

**Parcours manuel vérifié** avec la pile Docker (voir 07) :
- connexion de `alice` pour `acme` avec consentement ;
- claims `org_id`, `org_roles`, `aud=demo-api` ;
- API de démo : 200 pour sa propre organisation, 403 pour une autre ;
- `organization=initech` : `access_denied` renvoyé au client.
