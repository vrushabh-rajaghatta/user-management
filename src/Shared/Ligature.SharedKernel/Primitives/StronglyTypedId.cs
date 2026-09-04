using Ligature.SharedKernel.Exceptions;

namespace Ligature.SharedKernel.Primitives;

public abstract class StronglyTypedId : IEquatable<StronglyTypedId>
{
    protected StronglyTypedId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException(
                "A strongly typed id cannot be empty.");
        }

        Value = value;
    }

    public Guid Value { get; }

    public bool Equals(StronglyTypedId? other)
    {
        return other is not null
            && GetType() == other.GetType()
            && Value.Equals(other.Value);
    }

    public override bool Equals(object? obj)
    {
        return obj is StronglyTypedId other
            && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(GetType(), Value);
    }

    public override string ToString()
    {
        return Value.ToString();
    }

    public static bool operator ==(
        StronglyTypedId? left,
        StronglyTypedId? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(
        StronglyTypedId? left,
        StronglyTypedId? right)
    {
        return !Equals(left, right);
    }
}