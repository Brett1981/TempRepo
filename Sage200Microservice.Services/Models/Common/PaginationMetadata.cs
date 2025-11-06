using System;

namespace Sage200Microservice.Services.Models.Common
{
    /// <summary>
    /// Provides metadata for paginated responses.
    /// </summary>
    public class PaginationMetadata
    {
        /// <summary>
        /// The current page number (1-based).
        /// </summary>
        public int CurrentPage { get; set; }

        /// <summary>
        /// The number of items requested per page.
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// The total number of items available across all pages.
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// The total number of pages available.
        /// </summary>
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

        /// <summary>
        /// Indicates if there is a previous page.
        /// </summary>
        public bool HasPrevious => CurrentPage > 1;

        /// <summary>
        /// Indicates if there is a next page.
        /// </summary>
        public bool HasNext => CurrentPage < TotalPages;
    }
}