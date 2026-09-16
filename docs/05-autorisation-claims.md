# 05 - Rôles, permissions, claims et scopes

## 1. Modèle d'autorisation hybride

- **Permissions de l'IdP** : catalogue fixe (`PermissionCatalog`), chacune avec une portée.
  - Plateforme : `platform.users.manage`, `platform.organizations.manage`, `platform.clients.manage`, `platform.scopes.manage`, `platform.roles.manage`, `platform.security.manage`, `platform.audit.read`.
  - Organisation : `org.members.read`, `org.members.manage`, `org.invitations.manage`, `org.roles.manage`, `org.applications.manage`, `org.settings.manage`.
- **Rôles** : ensembles de permissions de même portée.
  - Rôles système : `Platform administrator` (toutes les permissions plateforme, resynchronisées à chaque démarrage) et `Organization administrator` (créé avec chaque organisation).
- **Contexte** : chaque contrôle est fait par `IAccessGuard` dans les services Application.
  - `RequirePlatformPermissionAsync(permission)`.
  - `RequireOrganizationPermissionAsync(organizationId, permission)` : exige une **adhésion active** et un rôle de **cette** organisation.
  - Une permission plateforme n'accorde **aucune** permission organisationnelle implicite (pas d'accès inter-tenant par défaut).
- **Contexte applicatif** : hors IdP (réponse B). Chaque application décide à partir du token et de ses propres données.

### Anti-escalade (`PrivilegeEscalationPolicy`)

1. Créer, modifier, supprimer, affecter ou retirer un rôle n'est permis que si **toutes** ses permissions sont détenues par l'acteur, dans la même portée et la même organisation.
2. Suspendre, retirer ou désactiver un utilisateur plus privilégié que soi est interdit.
3. Modifier ses propres adhésions ou affectations est interdit. Cela empêche l'auto-promotion et l'auto-verrouillage : il reste toujours au moins un administrateur, l'acteur lui-même.
4. Les areas OrgAdmin n'exposent aucune opération sur les rôles plateforme ; un rôle plateforme ne peut pas être construit dans une organisation (domaine).
5. Chaque refus est audité (`authz.escalation.denied`, `authz.cross_tenant.denied`).

### Isolation

- Les identifiants d'une autre organisation passés dans une route de sa propre organisation sont introuvables : repositories filtrés par `organizationId`, résultat `404` sous forme de toast.
- Une route pointant vers une autre organisation est refusée (`403`).

Tests : `AdministrationTests`.

## 2. Scopes

| Scope | Effet |
|---|---|
| `openid` | Obligatoire. `sub`, `org_id`, `org_slug` |
| `profile` | `name`, `given_name`, `family_name`, `preferred_username` (ID token et userinfo) |
| `email` | `email`, `email_verified` (ID token et userinfo) |
| `offline_access` | Refresh token (le client doit y être autorisé) |
| `org.roles` | `org_roles` : rôles de l'utilisateur **dans l'organisation validée** (ID token et access token) |
| scopes d'API (ex. `demo_api`) | Ajoute la resource (ex. `demo-api`) à `aud` de l'access token |

**Contrôle des scopes** :
- Chaque client a une liste de scopes autorisés, appliquée par OpenIddict (permissions `scp:`) **et** par `OidcAuthorizationService`.
- Un scope non autorisé renvoie `invalid_scope`.

## 3. Modèle de claims (`ClaimsPolicy`)

| Claim | ID token | Access token | Condition |
|---|---|---|---|
| `sub` | oui | oui | toujours |
| `org_id` | oui | oui | toujours (organisation validée) |
| `org_slug` | oui | non | toujours |
| `sid` | oui | non | session interactive |
| `name`, `given_name`, `family_name`, `preferred_username` | oui | non | `profile` |
| `email`, `email_verified` | oui | non | `email` |
| `org_roles` (multi-valué) | oui | oui | `org.roles` |
| `iss`, `aud`, `exp`, `iat`, `jti`, `client_id`, `scope` | ajoutés par OpenIddict | | |
| `cerberus_org` | non | non | privé (code et refresh token chiffrés) |

**Jamais émis** : rôles et permissions plateforme, permissions administratives de l'IdP, statut du compte, claims d'une autre organisation.

**Destinations** : définies claim par claim (`SetDestinations`). Tests : `ClaimsPolicyTests` et `FullFlow_ShouldIssueSignedTokensWithMinimalClaims`.
