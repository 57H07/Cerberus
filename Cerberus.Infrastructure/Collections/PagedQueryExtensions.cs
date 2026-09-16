using Cerberus.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Cerberus.Infrastructure.Collections;

internal static class PagedQueryExtensions
{
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(this IQueryable<T> query, PagedFilter filter, CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);
        var items = await query.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToListAsync(cancellationToken);
        return new PagedResult<T>(items, filter.Page, filter.PageSize, total);
    }
}
