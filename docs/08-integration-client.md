# 08 - Intégrer une application web cliente

## 1. Enregistrement (administrateur plateforme)

1. **Organisation propriétaire** : `/Admin/Organizations`. Créer l'organisation si besoin (le slug est immuable).
2. **Application** : `/Admin/Applications` → « New application », en choisissant l'organisation propriétaire. Accorder ensuite les autres organisations par leur slug.
3. **Scope d'API** : `/Admin/Scopes`, si l'application expose une API. Exemple : `crm_api` avec la resource `crm-api`.
4. **Client OIDC** : dans l'application, « New client ».
   - `Confidential` pour une application serveur ; `Public` pour une SPA ou une application native (PKCE requis dans tous les cas).
   - Redirect URI exacte, par exemple `https://crm.example.com/signin-oidc`, et post-logout URI.
   - Scopes autorisés : `openid profile email offline_access org.roles crm_api`, au besoin.
   - Politique de consentement : `Remembered` par défaut ; `Trusted` pour une application interne de confiance.
5. **Secret** : **copier le secret affiché une seule fois** et le stocker dans le coffre de l'application. En cas de perte, faire « rotate secret ».
6. **Accès côté organisation** : les administrateurs d'une organisation peuvent désactiver l'application pour leur organisation (`/OrgAdmin/{org}/Applications`).

## 2. Application ASP.NET Core (MVC / Razor)

```csharp
builder.Services.AddAuthentication(o =>
    {
        o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        o.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie()
    .AddOpenIdConnect(o =>
    {
        o.Authority = "https://idp.example.com/";
        o.ClientId = "crm-web";
        o.ClientSecret = builder.Configuration["Oidc:ClientSecret"]; // coffre / variable d'environnement
        o.ResponseType = "code";
        o.UsePkce = true;
        o.SaveTokens = true;
        o.MapInboundClaims = false;
        o.Scope.Clear();
        foreach (var s in new[] { "openid", "profile", "email", "offline_access", "org.roles", "crm_api" }) o.Scope.Add(s);
        o.TokenValidationParameters.NameClaimType = "name";
        o.TokenValidationParameters.RoleClaimType = "org_roles";

        // Organisation demandée : une intention, validée par Cerberus.
        o.Events.OnRedirectToIdentityProvider = ctx =>
        {
            if (ctx.Properties.Items.TryGetValue("organization", out var org) && !string.IsNullOrEmpty(org))
                ctx.ProtocolMessage.SetParameter("organization", org);
            return Task.CompletedTask;
        };
        o.Events.OnTokenValidated = ctx =>
        {
            if (ctx.Principal?.FindFirst("org_id") is null) ctx.Fail("No validated organization.");
            return Task.CompletedTask;
        };
    });
```

Déclencher la connexion pour une organisation précise :

```csharp
return Challenge(new AuthenticationProperties(new Dictionary<string, string?> { ["organization"] = "acme" }) { RedirectUri = "/" },
    OpenIdConnectDefaults.AuthenticationScheme);
```

Sans le paramètre `organization`, Cerberus choisit automatiquement si une seule organisation est éligible, sinon affiche un écran de sélection. La valeur retenue est **toujours** celle du claim `org_id`, jamais celle demandée.

**Erreurs à gérer** (`OnRemoteFailure`) : `access_denied` (organisation non disponible ou consentement refusé), `consent_required` / `login_required` (avec `prompt=none`).

**Déconnexion** :

```csharp
SignOut(CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme)
```

Cerberus demande une confirmation, puis redirige vers la post-logout URI enregistrée.

## 3. API (resource server)

```csharp
builder.Services.AddAuthentication().AddJwtBearer(o =>
{
    o.Authority = "https://idp.example.com/";
    o.Audience = "crm-api";                       // resource du scope d'API
    o.MapInboundClaims = false;
    o.TokenValidationParameters.ValidTypes = ["at+jwt"];
    o.TokenValidationParameters.RoleClaimType = "org_roles";
});
```

La validation porte sur la signature (JWKS, `kid`), l'issuer, l'audience et l'expiration (5 min).

**Autorisation locale obligatoire** (réponse C) :
- **Organisation** : vérifier que chaque ressource appartient à l'organisation du token (`org_id`). Ne jamais se fier à un identifiant d'organisation fourni par le client HTTP (voir `OrdersApiController` du client de démo).
- **Rôles** : traduire `org_roles` en permissions métier **propres à l'application**. Un rôle dans le token n'autorise pas automatiquement l'accès à toutes les ressources de l'organisation.
- **Opérations sensibles** : revérifier l'état métier (compte actif, adhésion...) côté application, ou raccourcir la fenêtre de validité. Un access token révoqué reste utilisable jusqu'à 5 min.
- **Tokens** : n'accepter que des access tokens, jamais un ID token.

## 4. Refresh

- Avec `offline_access`, le refresh token (8 h, rotation) permet de renouveler l'access token.
- Chaque refresh est revalidé par Cerberus : utilisateur, adhésion, organisation, client, consentement.
- En cas d'`invalid_grant`, supprimer la session locale et relancer une authentification.
