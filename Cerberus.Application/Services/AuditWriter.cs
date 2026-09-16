using System.Text.Json;
using Cerberus.Application.Interfaces.Repositories;
using Cerberus.Application.Interfaces.Services;
using Cerberus.Domain.Auditing;

namespace Cerberus.Application.Services;

public interface IAuditWriter
{
    /// <summary>Adds an audit entry to the current unit of work (saved with the use case changes).</summary>
    void Write(string action, AuditOutcome outcome = AuditOutcome.Success, Guid? organizationId = null,
        string? targetType = null, object? targetId = null, object? details = null, Guid? actorUserId = null);
}

public class AuditWriter(IAuditRepository auditRepository, IActorContext actor, IClock clock) : IAuditWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public void Write(string action, AuditOutcome outcome = AuditOutcome.Success, Guid? organizationId = null,
        string? targetType = null, object? targetId = null, object? details = null, Guid? actorUserId = null)
    {
        // Callers must only pass non-sensitive details (never secrets, passwords or tokens).
        var serialized = details is null ? null : JsonSerializer.Serialize(details, SerializerOptions);
        auditRepository.Add(AuditEntry.Create(
            action,
            outcome,
            actorUserId ?? actor.UserId,
            organizationId,
            targetType,
            targetId?.ToString(),
            actor.IpAddress,
            serialized,
            clock.UtcNow));
    }
}
