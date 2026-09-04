using Ligature.SharedKernel.Primitives;

public abstract class Entity<TId> where TId : StronglyTypedId
{
    protected Entity(TId id) => Id = id;

    // EF Core materialization only. Domain code uses the constructor above.
    protected Entity() => Id = default!;

    public TId Id { get; }
}