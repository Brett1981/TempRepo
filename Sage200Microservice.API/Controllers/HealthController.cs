using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sage200Microservice.Data;
using Sage200Microservice.Data.Models;

namespace Sage200Microservice.API.Controllers
{
    /// <summary>
    /// Controller for checking the health of the microservice
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class HealthController : ControllerBase
    {
        private readonly ApplicationContext _context;

        /// <summary>
        /// Initializes a new instance of the HealthController
        /// </summary>
        /// <param name="context"> The database context </param>
        public HealthController(ApplicationContext context)
        {
            _context = context;
        }

        /// <summary>
        /// GET /api/health/links
        /// Lightweight probe to verify ExternalIdLinks table, indexes and row counts.
        /// </summary>
        [HttpGet("links")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetLinksHealth(CancellationToken ct)
        {
            var provider = _context.Database.ProviderName ?? "unknown";

            long total;
            try
            {
                total = await _context.ExternalIdLinks.LongCountAsync(ct);
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    status = "error",
                    provider,
                    message = "ExternalIdLinks not accessible",
                    exception = ex.GetType().Name,
                    details = ex.Message
                });
            }

            var withSageId = await _context.ExternalIdLinks.LongCountAsync(x => x.SageId != null, ct);
            var withSageUrn = await _context.ExternalIdLinks.LongCountAsync(x => x.SageUrn != null, ct);

            return Ok(new
            {
                status = "ok",
                provider,
                counts = new { total, withSageId, withSageUrn }
            });
        }

        /// <summary>
        /// GET /api/health/keys
        /// Quick check that ApiKeys exist, so ExternalIdLinks inserts won't hit FK.
        /// </summary>
        [HttpGet("keys")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetApiKeysHealth(CancellationToken ct)
        {
            var count = await _context.Set<ApiKey>().CountAsync(ct);

            var sample = await _context.Set<ApiKey>()
                .AsNoTracking()
                .OrderBy(k => k.Id)
                .Select(k => new { k.Id })
                .Take(3)
                .ToListAsync(ct);

            return Ok(new { status = "ok", apiKeys = new { count, sample } });
        }

        /// <summary>
        /// Gets the health status of the microservice
        /// </summary>
        /// <returns> The health status </returns>
        /// <response code="200"> Returns the health status </response>
        /// <response code="500"> If there was an error checking the health status </response>
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        [ProducesResponseType(typeof(HealthStatus), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(HealthStatus), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<HealthStatus>> Get(CancellationToken ct)
        {
            try
            {
                // Check database connectivity
                var canConnect = await _context.Database.CanConnectAsync(ct);

                // Check if migrations are applied
                var pendingMigrations = await _context.Database.GetPendingMigrationsAsync(ct);
                var hasPendingMigrations = pendingMigrations.Any();

                return Ok(new HealthStatus
                {
                    Status = "Healthy",
                    DatabaseConnected = canConnect,
                    HasPendingMigrations = hasPendingMigrations,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new HealthStatus
                {
                    Status = "Unhealthy",
                    DatabaseConnected = false,
                    ErrorMessage = ex.Message,
                    Timestamp = DateTime.UtcNow
                });
            }
        }
    }

    /// <summary>
    /// Response model for health status
    /// </summary>
    public class HealthStatus
    {
        /// <summary>
        /// The overall status of the microservice
        /// </summary>
        /// <example> Healthy </example>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Indicates whether the database connection is working
        /// </summary>
        public bool DatabaseConnected { get; set; }

        /// <summary>
        /// Indicates whether there are pending database migrations
        /// </summary>
        public bool HasPendingMigrations { get; set; }

        /// <summary>
        /// The error message if the status is unhealthy
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// The timestamp of the health check
        /// </summary>
        public DateTime Timestamp { get; set; }
    }
}
