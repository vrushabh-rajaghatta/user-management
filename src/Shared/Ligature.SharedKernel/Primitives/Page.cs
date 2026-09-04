namespace Ligature.SharedKernel.Primitives;

public sealed record Page<T>(
    IReadOnlyList<T> Items,
    int PageNumber,
    int PageSize,
    int TotalCount)
{
    public int TotalPages =>
        PageSize == 0
            ? 0
            : (int)Math.Ceiling(
                TotalCount / (double)PageSize);

    public bool HasPreviousPage
        => PageNumber > 1;

    public bool HasNextPage
        => PageNumber < TotalPages;
}