namespace Cerberus.Domain.Common;

public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();

    public DateTime CreatedAt { get; protected set; }

    public DateTime? UpdatedAt { get; protected set; }

    protected Entity()
    {
    }

    protected Entity(DateTime utcNow)
    {
        CreatedAt = Check.Utc(utcNow, nameof(utcNow));
    }

    protected void Touch(DateTime utcNow) => UpdatedAt = Check.Utc(utcNow, nameof(utcNow));
}
