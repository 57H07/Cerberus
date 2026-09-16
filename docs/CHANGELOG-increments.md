# Journal des incréments

Chaque incrément a été vérifié par :
- `dotnet build Cerberus.slnx -warnaserror` (0 avertissement) ;
- `dotnet test` ;
- `dotnet ef migrations has-pending-model-changes` (« No changes ») quand la persistance était concernée.

| # | Incrément | Fichiers principaux | Vérification | Risques ou limites restants |
|---|---|---|---|---|
| 0 | Squelette | `Cerberus.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitignore` | Build | |
| 1 | Domaine | `Cerberus.Domain/**` | 78 tests domaine | |
| 2 | Persistance | `Infrastructure/Data/**`, `Repositories/**`, migration `InitialCreate` | Migration générée, aucun changement en attente, application réelle sur SQL Server (Testcontainers et Docker) | Révocation par organisation via `LIKE` JSON |
| 3 | Authentification | `Infrastructure/Identity/**`, `Application/Services/UserAuthenticationService.cs`, `Web/Controllers/AccountController.cs`, `SessionValidationCookieEvents` | `AuthenticationTests` (13) | Rate limiting en mémoire |
| 4 | OIDC | `Web/Extensions/WebServiceCollectionExtensions.cs`, `Web/Controllers/AuthorizationController.cs`, `Infrastructure/Oidc/**`, `Infrastructure/Security/**`, `Application/Oidc/**` | `OidcFlowTests` (15) | Rotation au redémarrage |
| 5 | Client de démo | `Cerberus.DemoClient/**`, Dockerfiles, `docker-compose.yml` | Parcours E2E scripté sur la pile Docker. Correctifs issus de ce test : réécriture du discovery back-channel, CSP pour `form_post` | Réécriture back-channel propre à Docker |
| 6 | Multi-tenancy | `Domain/Organizations/TenantAccessRules.cs`, `OidcAuthorizationService` (sélection, revalidation) | Tests tenant (domaine, application, intégration) | |
| 7 | Autorisation | `Application/Authorization/**`, services d'administration | `AccessGuardTests`, `AdministrationTests` | |
| 8 | Administration | `Web/Areas/Admin/**`, `Web/Areas/OrgAdmin/**` | `AdministrationTests` (9) | UI minimale |
| 9 | Audit et durcissement | `AuditWriter`, `SecurityHeadersMiddleware`, `GlobalExceptionMiddleware`, tests d'architecture | `ArchitectureTests` (4) ; en-têtes testés | Clés Data Protection non chiffrées (local) |

**Total** : 144 tests automatisés, tous au vert à la dernière exécution.
