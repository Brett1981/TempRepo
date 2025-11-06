using System.Collections.Generic;

namespace Sage200Microservice.Services.Models.Common
{
    /// <summary>
    /// Represents a paginated response containing a list of items and pagination metadata.
    /// </summary>
    /// <typeparam name="T">The type of the items in the list.</typeparam>
    public class PaginatedDataResponse<T>
    {
        /// <summary>
        /// Metadata describing the pagination details.
        /// </summary>
        public PaginationMetadata Metadata { get; set; } = default!;

        /// <summary>
        /// The list of items for the current page.
        /// </summary>
        public List<T> Items { get; set; } = new List<T>();
    }
}