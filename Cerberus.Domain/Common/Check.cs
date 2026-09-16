using Cerberus.Domain.Exceptions;

namespace Cerberus.Domain.Common;

internal static class Check
{
    public static string Required(string? value, string fieldName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException($"{fieldName} is required.", fieldName);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new DomainValidationException($"{fieldName} must not exceed {maxLength} characters.", fieldName);
        }

        return trimmed;
    }

    public static string? Optional(string? value, string fieldName, int maxLength)
    {
        return string.IsNullOrWhiteSpace(value) ? null : Required(value, fieldName, maxLength);
    }

    public static Guid NotEmpty(Guid value, string fieldName)
    {
        if (value == Guid.Empty)
        {
            throw new DomainValidationException($"{fieldName} is required.", fieldName);
        }

        return value;
    }

    public static DateTime Utc(DateTime value, string fieldName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new DomainValidationException($"{fieldName} must be expressed in UTC.", fieldName);
        }

        return value;
    }
}
