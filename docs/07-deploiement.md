# 07 - Déploiement local

## 1. Prérequis

- Docker Desktop.
- SDK .NET 10 : pour les tests et l'export du certificat.
- Certificat HTTPS de développement exporté (le dossier `certs/` est ignoré par git) :

```bash
dotnet dev-certs https --trust
```

```bash
dotnet dev-certs https -ep certs/cerberus-dev.pfx -p <DEV_CERT_PASSWORD>
```

- Fichier `.env` créé à partir de `.env.example`, avec **toutes** les valeurs remplacées. Le fichier `.env` est ignoré par git.

## 2. Lancement

```bash
docker compose up --build -d
```

| Service | URL |
|---|---|
| Cerberus (IdP, administration) | https://localhost:5001 |
| Client de démonstration | https://localhost:5003 |
| Mailpit (emails : reset, invitations) | http://localhost:8025 |
| SQL Server | localhost,14333 (utilisateur `sa`) |

Au premier démarrage, avec `ASPNETCORE_ENVIRONMENT=Development` :
1. Migrations appliquées (`Database__MigrateOnStartup=true`).
2. Rôle système `Platform administrator` synchronisé, puis clés de signature et de chiffrement créées si absentes.
3. **Administrateur initial** créé uniquement si aucun administrateur plateforme n'existe, à partir de `BOOTSTRAP_ADMIN_*`. La création est auditée et journalisée. Retirer ensuite `BOOTSTRAP_ADMIN_PASSWORD` du `.env`.
4. **Données de démo**, uniquement en Development : organisations `acme`, `globex`, `initech` ; utilisateurs `alice` (admin d'acme, membre de globex) et `bob` (membre acme avec rôle `Sales`, membre initech) ; client `demo-client` accordé à acme et globex, pas à initech ; scope `demo_api`. Le mot de passe est `DEMO_USER_PASSWORD`.

Parcours de démonstration :
1. Sur https://localhost:5003, choisir « Sign in for acme ».
2. Se connecter avec `alice`, consentir.
3. Appeler l'API : 200 pour sa propre organisation, puis 403 pour une autre organisation.
4. « Sign in for initech » : `access_denied`.

## 3. Sans Docker (IdP et client de démo sur l'hôte)

```bash
docker compose up -d sqlserver mailpit
```

```bash
dotnet user-secrets --project Cerberus.Web set "ConnectionStrings:DefaultConnection" "Server=localhost,14333;Database=Cerberus;User Id=sa;Password=<SQL_SA_PASSWORD>;TrustServerCertificate=true"
```

Définir de la même façon, avec `dotnet user-secrets --project Cerberus.Web set` :
- `Bootstrap:AdminEmail`, `Bootstrap:AdminUserName`, `Bootstrap:AdminPassword` ;
- `Seed:DemoUserPassword` ;
- `Seed:DemoClientSecret`.

Côté client de démo :

```bash
dotnet user-secrets --project Cerberus.DemoClient set "Oidc:ClientSecret" "<DEMO_CLIENT_SECRET>"
```

Puis lancer les deux applications :

```bash
dotnet run --project Cerberus.Web --launch-profile https
```

```bash
dotnet run --project Cerberus.DemoClient --launch-profile https
```

## 4. Configurations Development et Production

| Clé | Development | Production (`appsettings.json`) |
|---|---|---|
| `Database:MigrateOnStartup` | true | false : bundle de migration |
| `Seed:Enabled` | true | false ; refusé hors Development même si activé |
| `Oidc:Issuer` | `https://localhost:5001/` | URL publique obligatoire |
| Emails | Mailpit | SMTP avec STARTTLS |
| Exceptions | Page développeur | `/Home/Error` + HSTS |
| Secrets | `.env` / user-secrets | Variables d'environnement ou coffre (Key Vault...) |

**Migration en production** :

```bash
dotnet ef migrations bundle -p Cerberus.Infrastructure -s Cerberus.Infrastructure -o efbundle --self-contained
```

```bash
./efbundle --connection "<connection string>"
```

## 5. Particularités Docker

- **Deux adresses pour l'IdP** : le navigateur l'atteint sur `https://localhost:5001`, le conteneur du client sur `https://cerberus-web:8443`. L'issuer est fixé à l'URL publique.
- **Réécriture back-channel** : le `BackchannelHandler` du client de démo réécrit les appels serveur-à-serveur (discovery, JWKS, token) vers l'adresse interne, et le document de discovery vers l'adresse publique.
- **Certificat** : le certificat de dev (émis pour `localhost`) est épinglé par empreinte **uniquement en Development**.
- **Persistance** : les clés de signature et Data Protection sont en base (volume `sqldata`). Elles survivent aux redémarrages et aux reconstructions des conteneurs.

## 6. Limites du déploiement local et adaptations pour un environnement distant

Voir aussi 09.

**Limites du local** :
- Certificat auto-signé.
- Clés Data Protection non chiffrées au repos.
- Rate limiting en mémoire.
- Instance unique.
- Mailpit.
- Compte `sa`.

**Adaptations pour un environnement distant** :
- Certificat TLS public (reverse proxy ou ingress) ; configurer `ForwardedHeaders:KnownProxies`.
- `Oidc:Issuer` sur le nom public.
- Suppression de la réécriture back-channel (DNS résolvable par tous).
- `ProtectKeysWithCertificate` ou Azure Key Vault pour Data Protection ; idéalement signature par HSM ou Key Vault.
- Compte SQL dédié avec droits minimaux ; migrations par bundle dans le pipeline CD.
- Rate limiting distribué (Redis ou passerelle) ; sessions et caches partagés.
- Redémarrage progressif planifié pour la rotation des clés.
- SMTP authentifié.
- Collecte centralisée des journaux et alertes sur `authz.*` et `auth.lockout`.
