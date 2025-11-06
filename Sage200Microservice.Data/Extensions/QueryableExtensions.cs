using System.Linq.Expressions;
using System.Reflection;

namespace Sage200Microservice.Data.Extensions
{
    /// <summary>
    /// Extension methods for IQueryable
    /// </summary>
    public static class QueryableExtensions
    {
        /// <summary>
        /// Applies pagination to a query
        /// </summary>
        /// <typeparam name="T"> The type of the query </typeparam>
        /// <param name="query">    The query </param>
        /// <param name="page">     The page number (1-based) </param>
        /// <param name="pageSize"> The number of items per page </param>
        /// <returns> The paginated query </returns>
        public static IQueryable<T> ApplyPaging<T>(this IQueryable<T> query, int page, int pageSize)
        {
            if (page <= 0)
            {
                page = 1;
            }

            if (pageSize <= 0)
            {
                pageSize = 10;
            }

            return query.Skip((page - 1) * pageSize).Take(pageSize);
        }

        /// <summary>
        /// Applies sorting to a query
        /// </summary>
        /// <typeparam name="T"> The type of the query </typeparam>
        /// <param name="query">         The query </param>
        /// <param name="sortBy">        The property name to sort by </param>
        /// <param name="sortDirection"> The sort direction (asc or desc) </param>
        /// <returns> The sorted query </returns>
        public static IQueryable<T> ApplySorting<T>(this IQueryable<T> query, string sortBy, string sortDirection)
        {
            if (string.IsNullOrEmpty(sortBy))
            {
                return query;
            }

            // Get the property info for the sort property
            var propertyInfo = typeof(T).GetProperty(sortBy, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
            if (propertyInfo == null)
            {
                return query;
            }

            // Create a parameter expression for the lambda
            var parameter = Expression.Parameter(typeof(T), "x");

            // Create a property access expression
            var property = Expression.Property(parameter, propertyInfo);

            // Create a lambda expression for the property
            var lambda = Expression.Lambda(property, parameter);

            // Determine the method to call based on the sort direction
            string methodName = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase)
                ? "OrderByDescending"
                : "OrderBy";

            // Create a generic method for the appropriate sort method
            var method = typeof(Queryable).GetMethods()
                .Where(m => m.Name == methodName && m.IsGenericMethodDefinition)
                .Where(m => m.GetParameters().Length == 2)
                .Single();

            // Make the method generic for the entity type and the property type
            var genericMethod = method.MakeGenericMethod(typeof(T), propertyInfo.PropertyType);

            // Invoke the method to get the sorted query
            return (IQueryable<T>)genericMethod.Invoke(null, new object[] { query, lambda });
        }
    }
}