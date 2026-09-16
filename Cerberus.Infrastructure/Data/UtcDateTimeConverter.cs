using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Cerberus.Infrastructure.Data;

// SQL Server datetime2 loses DateTimeKind: every date is stored and read back as UTC.
internal sealed class UtcDateTimeConverter()
    : ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

internal sealed class NullableUtcDateTimeConverter()
    : ValueConverter<DateTime?, DateTime?>(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
