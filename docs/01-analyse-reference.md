# 01 - Analyse du dépôt de référence

Dépôt analysé : `57H07/CleanArchitecture`, branche `main`, commit `a96b25b` (clone superficiel en lecture seule).

## 1. Projets et dépendances

| Projet | Rôle | Références |
|---|---|---|
| `CleanArchitecture.Domain` | Entités, exceptions | aucune |
| `CleanArchitecture.Application` | Services, DTOs, interfaces | Domain |
| `CleanArchitecture.Infrastructure` | EF Core, repositories, UoW | Domain, Application |
| `CleanArchitecture.Web` | MVC + Razor | Application, Infrastructure |
| `CleanArchitecture.Application.Tests` | Tests unitaires | Application, Domain |

- Le sens des dépendances va vers l'intérieur.
- Solution `.sln` et projets à plat.

## 2. Versions et packages

- **Build** : `net10.0`, `Nullable`, `TreatWarningsAsErrors` et `EnforceCodeStyleInBuild`, définis dans `Directory.Build.props`.
- **Packages** : gestion centralisée des versions (`Directory.Packages.props`).
- **Dépendances principales** :
  - EF Core SQL Server 10
  - Mapster
  - AspNetCore.SassCompiler
  - xUnit, Moq, FluentAssertions 6.12 (dernière version sous licence Apache)

## 3. Architecture réellement utilisée

L'architecture est une Clean Architecture « services + repositories + Unit of Work ».

- **Absents** : MediatR, CQRS, Minimal APIs.
- **Application** : `IXxxService`/`XxxService`, DTOs `CreateXxxDto`/`XxxDto`/`XxxFilterDto`, `PagedResult`, mappings Mapster.
- **Infrastructure** :
  - `ApplicationDbContext` et `IEntityTypeConfiguration`
  - repositories qui ne sauvegardent jamais
  - `UnitOfWork` qui traduit les violations d'index unique en `DuplicateEntityException`
- **Web** :
  - contrôleurs MVC et vues Razor, Bootstrap 5
  - toasts via TempData
  - `GlobalExceptionMiddleware`
  - antiforgery global

## 4. Identité et authentification

**Aucune.**

- Ni ASP.NET Core Identity, ni cookie d'authentification, ni JWT, ni `[Authorize]`.
- `BaseEntity.CreatedBy/UpdatedBy` n'est renseigné que par le seed.

## 5. Éléments réutilisables et conservés

- Organisation en couches, CPM, options de build strictes.
- Conventions de nommage : `XxxService`, `XxxRepository`, `XxxConfiguration`, méthodes `...Async(ct = default)`.
- Extensions DI `AddApplication()` / `AddInfrastructure(configuration)`.
- Hiérarchie d'exceptions : `DomainException`, `ResourceNotFoundException`, `InsufficientRightsException`, et le middleware qui la traduit en HTTP.
- UoW avec traduction des index uniques.
- Toasts TempData, antiforgery global, Bootstrap.
- Style de tests : xUnit + Moq + FluentAssertions, nommage `Methode_Condition_ShouldResultat`.

## 6. Écarts identifiés et traitement

| Écart dans la référence | Traitement dans Cerberus |
|---|---|
| `EnsureDeleted()` + `EnsureCreated()` à chaque démarrage, dans tous les environnements | Vraies migrations EF commitées. Application au démarrage seulement si `Database:MigrateOnStartup` (Development) |
| `.gitignore` exclut `**/Migrations/` | Exclusion retirée |
| Seed via `HasData` avec `DateTime.UtcNow` (modèle non déterministe) | `HasData` réservé au catalogue de permissions (déterministe). Données de démo via `DatabaseInitializer`, en Development uniquement |
| `BaseEntity` avec Id `int` et DataAnnotations dans le Domain | `Entity` avec `Guid` v7, sans attributs, configuration Fluent |
| Invariants vérifiés manuellement (`ValidateBusinessRules()`) | Invariants imposés par les constructeurs et méthodes (setters privés) |
| Fautes d'orthographe `RessourceNotFound`, `ValidationDomaine` | Corrigées : `ResourceNotFoundException`, `DomainValidationException` |
| Pas d'`UseExceptionHandler` hors Development | Ajouté, avec ProblemDetails pour les appels JSON |
| Pas de Docker, de tests d'intégration ni de tests d'architecture | Ajoutés |
| Code de démonstration Product/Customer | Non repris |
| Mapster, SassCompiler | Non repris : mapping manuel explicite, peu volumineux ; CSS simple |
| Documentation (README, CLAUDE.md) en contradiction avec le code | Documentation réécrite à partir du code réel |

## 7. Risques et incompatibilités relevés

- **Stores Identity** : ASP.NET Core Identity impose ses propres entités (`IdentityUser`), ce qui est contraire à un Domain indépendant. Résolu par des **stores Identity personnalisés** sur l'entité `User` du domaine (voir 02).
- **Persistance mixte** : le pattern Unit of Work de la référence cohabite avec les managers OpenIddict et `UserManager`, qui persistent immédiatement. Les cas d'usage concernés ordonnent explicitement leurs écritures et utilisent une transaction si nécessaire (création de client).
- **Headers en multi-instance** : le rendu MVC et les toasts de la référence supposent une seule instance. Sans objet pour le déploiement local.
