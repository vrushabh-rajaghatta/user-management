namespace Ligature.SharedKernel.Primitives;

public abstract class ValueObject
{
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public override bool Equals(object? obj)
    {
        if (obj is not ValueObject other)
            return false;

        if (GetType() != other.GetType())
            return false;

        return GetEqualityComponents()
            .SequenceEqual(other.GetEqualityComponents());
    }

    public override int GetHashCode()
    {
        return GetEqualityComponents()
            .Aggregate(
                0,
                (current, component) =>
                    HashCode.Combine(current, component));
    }

    public static bool operator ==(
        ValueObject? left,
        ValueObject? right)
        => Equals(left, right);

    public static bool operator !=(
        ValueObject? left,
        ValueObject? right)
        => !Equals(left, right);
}