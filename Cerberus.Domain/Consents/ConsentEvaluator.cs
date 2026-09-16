using Cerberus.Domain.Clients;

namespace Cerberus.Domain.Consents;

public enum ConsentDecision
{
    /// <summary>Tokens can be issued without user interaction.</summary>
    Granted = 0,

    /// <summary>The consent screen must be displayed.</summary>
    Required = 1
}

/// <summary>
/// Pure consent policy: decides whether the consent screen is needed for a request.
/// <c>prompt=none</c> handling (returning <c>consent_required</c>) is left to the protocol layer.
/// </summary>
public static class ConsentEvaluator
{
    public static ConsentDecision Evaluate(
        OidcClient client,
        UserConsent? existingConsent,
        IReadOnlyCollection<string> requestedScopes,
        bool promptConsent,
        DateTime utcNow)
    {
        if (client.ConsentPolicy == ConsentPolicy.Trusted)
        {
            return ConsentDecision.Granted;
        }

        if (promptConsent || client.ConsentPolicy == ConsentPolicy.AlwaysPrompt)
        {
            return ConsentDecision.Required;
        }

        return existingConsent is not null && existingConsent.Covers(requestedScopes, utcNow)
            ? ConsentDecision.Granted
            : ConsentDecision.Required;
    }
}
