namespace Cerberus.Application.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < TotalPages;

    public PagedResult<TOut> Map<TOut>(Func<T, TOut> selector) => new(Items.Select(selector).ToList(), Page, PageSize, TotalCount);
}

public record PagedFilter
{
    public const int MaxPageSize = 100;

    private readonly int _page = 1;
    private readonly int _pageSize = 20;

    public int Page
    {
        get => _page;
        init => _page = Math.Max(1, value);
    }

    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = Math.Clamp(value, 1, MaxPageSize);
    }

    public string? Search { get; init; }
}

public sealed record AuditFilter : PagedFilter
{
    public Guid? OrganizationId { get; init; }
    public Guid? ActorUserId { get; init; }
    public string? ActionPrefix { get; init; }
}
