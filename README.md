# Cerberus

Identity Provider OAuth 2.0 / OpenID Connect **multi-tenant**, en .NET 10 et Clean Architecture, basé sur **OpenIddict 7**, ASP.NET Core Identity (stores personnalisés) et SQL Server.

- **Utilisateurs** : identité globale, adhésion à plusieurs organisations.
- **Tenant** : demandé par l'application (paramètre `organization`) et **toujours validé côté serveur** ; claims limités à l'organisation validée.
- **Flux** : Authorization Code + PKCE obligatoire ; JWT RS256 courts ; refresh tokens tournants revalidés.
- **Consentement** : par (utilisateur, client, organisation), avec quatre politiques configurables par client.
- **Administration** : plateforme et organisation, avec anti-escalade de privilèges et audit.

## Démarrage rapide

1. Copier `.env.example` vers `.env` et définir toutes les valeurs.
2. Exporter le certificat de développement :
   ```bash
   dotnet dev-certs https -ep certs/cerberus-dev.pfx -p <DEV_CERT_PASSWORD>
   ```
3. Lancer la pile :
   ```bash
   docker compose up --build -d
   ```
4. Ouvrir :
   - https://localhost:5001 : Cerberus (compte bootstrap défini dans `.env`) ;
   - https://localhost:5003 : client de démonstration (`alice` / `bob`, mot de passe `DEMO_USER_PASSWORD`) ;
   - http://localhost:8025 : Mailpit.

## Tests

```bash
dotnet test Cerberus.slnx
```

Docker doit être démarré : les tests d'intégration utilisent Testcontainers SQL Server.

## Structure

| Projet | Contenu |
|---|---|
| `Cerberus.Domain` | Entités, invariants, règles de tenant et de consentement |
| `Cerberus.Application` | Cas d'usage, contrôle d'accès, anti-escalade, politique de claims |
| `Cerberus.Infrastructure` | EF Core, stores Identity, OpenIddict (registre, révocation), clés, SMTP, initialisation |
| `Cerberus.Web` | Serveur OIDC (passthrough), pages de compte, areas `Admin` et `OrgAdmin` |
| `Cerberus.DemoClient` | Application web cliente et API protégée d'exemple |
| `tests/*` | Domaine, Application, Architecture, Intégration |

## Documentation

1. [Analyse du dépôt de référence](docs/01-analyse-reference.md)
2. [Architecture, décisions, choix OIDC](docs/02-architecture.md)
3. [Domaine et persistance](docs/03-domaine-persistance.md)
4. [Protocole, flux, tenant, consentement, tokens et clés](docs/04-oidc-flux.md)
5. [Rôles, permissions, claims et scopes](docs/05-autorisation-claims.md)
6. [Sécurité et plan de tests](docs/06-securite-tests.md)
7. [Déploiement local](docs/07-deploiement.md)
8. [Intégration d'une application cliente](docs/08-integration-client.md)
9. [Limites et évolutions](docs/09-limites-evolutions.md)
10. [Journal des incréments](docs/CHANGELOG-increments.md)
