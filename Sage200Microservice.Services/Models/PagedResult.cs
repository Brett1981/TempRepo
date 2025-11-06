using System;
using System.Collections.Generic;

namespace Sage200Microservice.Services.Models
{
    /// <summary>
    /// Represents a single page of results from a larger collection.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    public sealed class PagedResult<T>
    {
        /// <summary>
        /// The items on the current page.
        /// </summary>
        public IReadOnlyList<T> Items { get; }

        /// <summary>
        /// The total number of records in the full result set (not just this page).
        /// </summary>
        public int TotalCount { get; }

        /// <summary>
        /// The 1-based page number of this result set.
        /// </summary>
        public int PageNumber { get; }

        /// <summary>
        /// The page size used to produce this result set.
        /// </summary>
        public int PageSize { get; }

        /// <summary>
        /// The total number of pages for this result set, given the <see cref="TotalCount"/> and <see cref="PageSize"/>.
        /// </summary>
        public int TotalPages => PageSize <= 0
            ? 1
            : (int)Math.Ceiling((double)TotalCount / PageSize);

        /// <summary>
        /// Creates a paged result using the standard (items, totalCount, pageNumber, pageSize) signature.
        /// </summary>
        /// <param name="items">Items on the current page.</param>
        /// <param name="totalCount">Total number of records across all pages.</param>
        /// <param name="pageNumber">1-based current page number.</param>
        /// <param name="pageSize">Requested page size.</param>
        public PagedResult(IReadOnlyList<T> items, int totalCount, int pageNumber, int pageSize)
        {
            Items = items ?? Array.Empty<T>();
            TotalCount = totalCount < 0 ? 0 : totalCount;
            PageNumber = pageNumber <= 0 ? 1 : pageNumber;
            PageSize = pageSize < 0 ? 0 : pageSize;
        }

        // --------------------------------------------------------------------
        // If you already have an existing ctor (e.g., items + totalCount only),
        // KEEP it to avoid breaking other call sites. Example shown below.
        // --------------------------------------------------------------------

        /// <summary>
        /// Creates a paged result when only items and total count are known.
        /// PageNumber defaults to 1 and PageSize defaults to the size of items.
        /// </summary>
        public PagedResult(IReadOnlyList<T> items, int totalCount)
            : this(items, totalCount, 1, items?.Count ?? 0)
        {
        }
    }
}