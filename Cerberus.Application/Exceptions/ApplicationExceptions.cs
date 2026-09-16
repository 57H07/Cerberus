using Cerberus.Domain.Exceptions;

namespace Cerberus.Application.Exceptions;

public sealed class EntityNotFoundException(string entityName, object id)
    : ResourceNotFoundException($"{entityName} '{id}' was not found.")
{
    public string EntityName { get; } = entityName;
}

public sealed class DuplicateEntityException(string entityName, string fieldDescription)
    : DomainException($"A {entityName} with the same {fieldDescription} already exists.")
{
    public string EntityName { get; } = entityName;
    public string FieldDescription { get; } = fieldDescription;
}

public sealed class BusinessRuleViolationException(string message) : DomainException(message);

/// <summary>
/// The actor is authenticated but not allowed to perform the operation (missing permission,
/// cross-organization access or privilege escalation attempt). Mapped to HTTP 403.
/// </summary>
public sealed class ForbiddenAccessException(string message) : InsufficientRightsException(message);
