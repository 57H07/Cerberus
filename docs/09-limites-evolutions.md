# 09 - Limites connues et évolutions possibles

## Limites (faits vérifiés)

| Limite | Détail |
|---|---|
| Révocation des access tokens | JWT auto-portés : valides jusqu'à 5 min après révocation. Pas d'endpoint d'introspection exposé |
| Rotation des clés | Chargées au démarrage : une nouvelle clé active n'est utilisée qu'après redémarrage (avertissement journalisé, rétention de 30 j) |
| Clés Data Protection | Stockées en base **non chiffrées** (avertissement ASP.NET Core au démarrage) : lire la base donne accès aux clés privées de signature |
| Rate limiting | En mémoire, par IP, par instance ; `ForwardedHeaders` à configurer derrière un proxy |
| Unit of Work | `UserManager` et les managers OpenIddict persistent immédiatement. Une panne entre deux écritures peut laisser un état partiel (atténué par transaction pour la création de client) |
| Révocation par organisation | Recherche des autorisations par propriété JSON (`LIKE`) : acceptable en volume modéré, à indexer au-delà |
| Consentement `AlwaysPrompt` | Le consentement est tout de même mémorisé pour la revalidation au refresh |
| Emails | Pas de file d'attente ni de retry : un échec SMTP fait échouer la requête d'invitation (après sauvegarde) |
| UI | Anglais uniquement, sans pagination des membres, sans recherche avancée ; pas de gestion de profil par l'utilisateur (hors sessions et consentements) |
| Rôles système d'organisation | Seul `Organization administrator` est créé automatiquement |
| Suppression | Pas de suppression physique d'utilisateurs ni d'organisations (désactivation ou suspension uniquement) ; pas de purge RGPD |
| Journal d'audit | Sans rétention ni export ; lisible par les administrateurs de l'organisation pour leur organisation seulement |

## Écarts assumés par rapport au plan initial

- **Introspection** : pas d'endpoint d'introspection (aucun client n'en a besoin, surface réduite).
- **Clés Data Protection** : persistées en base plutôt que dans un volume Docker, ce qui est plus robuste pour plusieurs instances.
- **Mapster et SassCompiler** : non repris de la référence.
- **Interfaces de repositories** : regroupées dans un seul fichier `IRepositories.cs` (la référence utilise un fichier par interface).

## Évolutions possibles

1. **Sécurité renforcée** :
   - MFA (TOTP, WebAuthn/passkeys) : Identity dispose des stores ; ajouter `IUserTwoFactorStore`.
   - `ProtectKeysWithCertificate` ou Key Vault, signature HSM.
   - DPoP ou mTLS pour les tokens (supportés par OpenIddict).
   - PAR obligatoire pour les clients confidentiels.
2. **Révocation fine** : introspection pour les API sensibles, ou tokens de référence pour certains clients ; back-channel logout (OIDC Back-Channel Logout).
3. **Rotation à chaud** : reconfiguration des `SigningCredentials` sans redémarrage, ou certificats X.509 avec dates de validité.
4. **Fédération** : connexion via Entra ID, Google, SAML par organisation (home realm discovery à partir du domaine email).
5. **Multi-tenancy avancé** :
   - politiques de sécurité par organisation (MFA exigée, durées de session) ;
   - domaines email vérifiés et adhésion automatique ;
   - rôles système personnalisables.
6. **Exploitation** : health checks (base, clés), OpenTelemetry, export d'audit (SIEM), rétention, rate limiting distribué, e-mails asynchrones (outbox).
7. **Self-service** : inscription publique (si souhaitée), gestion du profil, changement d'email avec confirmation, changement de mot de passe.
