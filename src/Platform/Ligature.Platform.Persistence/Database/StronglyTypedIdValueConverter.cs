using Ligature.SharedKernel.Primitives;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ligature.Platform.Persistence.Database;

public sealed class StronglyTypedIdValueConverter<TId>
    : ValueConverter<TId, Guid>
    where TId : StronglyTypedId
{
    public StronglyTypedIdValueConverter()
        : base(
            id => id.Value,
            value => Create(value))
    {
    }

    private static TId Create(Guid value)
    {
        return (TId)Activator.CreateInstance(
            typeof(TId),
            value)!;
    }
}