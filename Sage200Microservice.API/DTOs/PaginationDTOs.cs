namespace Sage200Microservice.API.DTOs
{
    /// <summary>
    /// DTO for pagination request
    /// </summary>
    public class PaginationRequestDto
    {
        private int _page = 1;
        private int _pageSize = 10;
        private const int MaxPageSize = 100;

        /// <summary>
        /// The page number (1-based)
        /// </summary>
        /// <example> 1 </example>
        public int Page
        {
            get => _page;
            set => _page = value < 1 ? 1 : value;
        }

        /// <summary>
        /// The number of items per page
        /// </summary>
        /// <example> 10 </example>
        public int PageSize
        {
            get => _pageSize;
            set => _pageSize = value > MaxPageSize ? MaxPageSize : (value < 1 ? 10 : value);
        }

        /// <summary>
        /// The sort field
        /// </summary>
        /// <example> CreatedAt </example>
        public string SortBy { get; set; } = "CreatedAt";

        /// <summary>
        /// The sort direction (asc or desc)
        /// </summary>
        /// <example> desc </example>
        public string SortDirection { get; set; } = "desc";
    }

    /// <summary>
    /// DTO for filter request
    /// </summary>
    public class FilterRequestDto : PaginationRequestDto
    {
        /// <summary>
        /// The search term for text-based filtering
        /// </summary>
        /// <example> acme </example>
        public string SearchTerm { get; set; }

        /// <summary>
        /// The start date for date range filtering
        /// </summary>
        /// <example> 2023-01-01 </example>
        public DateTime? StartDate { get; set; }

        /// <summary>
        /// The end date for date range filtering
        /// </summary>
        /// <example> 2023-12-31 </example>
        public DateTime? EndDate { get; set; }

        /// <summary>
        /// The status filter
        /// </summary>
        /// <example> active </example>
        public string Status { get; set; }
    }

    /// <summary>
    /// DTO for paginated response
    /// </summary>
    /// <typeparam name="T"> The type of items in the response </typeparam>
    public class PaginatedResponseDto<T>
    {
        /// <summary>
        /// The current page number
        /// </summary>
        public int Page { get; set; }

        /// <summary>
        /// The number of items per page
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// The total number of items
        /// </summary>
        public int TotalItems { get; set; }

        /// <summary>
        /// The total number of pages
        /// </summary>
        public int TotalPages { get; set; }

        /// <summary>
        /// Whether there is a previous page
        /// </summary>
        public bool HasPreviousPage => Page > 1;

        /// <summary>
        /// Whether there is a next page
        /// </summary>
        public bool HasNextPage => Page < TotalPages;

        /// <summary>
        /// The items in the current page
        /// </summary>
        public IEnumerable<T> Items { get; set; }

        /// <summary>
        /// Initializes a new instance of the PaginatedResponseDto class
        /// </summary>
        /// <param name="items">      The items in the current page </param>
        /// <param name="totalItems"> The total number of items </param>
        /// <param name="page">       The current page number </param>
        /// <param name="pageSize">   The number of items per page </param>
        public PaginatedResponseDto(IEnumerable<T> items, int totalItems, int page, int pageSize)
        {
            Page = page;
            PageSize = pageSize;
            TotalItems = totalItems;
            TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            Items = items;
        }
    }
}