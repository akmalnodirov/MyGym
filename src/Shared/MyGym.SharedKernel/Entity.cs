namespace MyGym.SharedKernel;

public abstract class Entity<TId>(TId id) : IEquatable<Entity<TId>>
    where TId : notnull
{
    public TId Id { get; private set; } = id;

    public bool Equals(Entity<TId>? other) =>
        other is not null && other.GetType() == GetType() && other.Id.Equals(Id);

    public override bool Equals(object? obj) => obj is Entity<TId> other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
}
