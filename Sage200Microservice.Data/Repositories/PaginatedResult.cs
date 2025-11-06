namespace Sage200Microservice.Data.Repositories
{
    public class PaginatedResult<T> : List<T>
    {
        public int TotalCount { get; }
        public int Page { get; }
        public int PageSize { get; }

        public PaginatedResult(IEnumerable<T> items, int totalCount, int page, int pageSize)
            : base(items)
        {
            TotalCount = totalCount;
            Page = page;
            PageSize = pageSize;
        }

        // Optional convenience; the controller currently uses enumeration directly.
        public IReadOnlyList<T> Items => this;
    }
}