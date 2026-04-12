using Marten.Pagination;

namespace Kalandra.Api.Infrastructure;

public record PaginationMetadata(
    int TotalCount,
    int Page,
    int PageSize,
    int PageCount,
    bool HasNextPage,
    bool HasPreviousPage)
{
    public static PaginationMetadata FromPagedList<T>(IPagedList<T> pagedList) => new(
        TotalCount: (int)pagedList.TotalItemCount,
        Page: (int)pagedList.PageNumber,
        PageSize: (int)pagedList.PageSize,
        PageCount: (int)pagedList.PageCount,
        HasNextPage: pagedList.HasNextPage,
        HasPreviousPage: pagedList.HasPreviousPage);
}
