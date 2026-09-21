using SKSMCorp.SharedKernel.Primitives;

namespace SKSMCorp.SharedKernel.Abstractions;

public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : StronglyTypedId
{
    protected AggregateRoot(TId id)
        : base(id)
    {
    }

    // EF Core materialization only.
    protected AggregateRoot()
        : base()
    {
    }
}