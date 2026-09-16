namespace Cerberus.Domain.Exceptions;

public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message)
    {
    }
}

public abstract class ResourceNotFoundException : DomainException
{
    protected ResourceNotFoundException(string message) : base(message)
    {
    }
}

public abstract class InsufficientRightsException : DomainException
{
    protected InsufficientRightsException(string message) : base(message)
    {
    }
}

public sealed class DomainValidationException(string message, string fieldName) : DomainException(message)
{
    public string FieldName { get; } = fieldName;
}

public sealed class InvalidDomainOperationException(string message) : DomainException(message);
