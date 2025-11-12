using Microsoft.AspNetCore.Mvc;
using Sage200Microservice.API.DTOs;
using Sage200Microservice.Data.Models;
using Sage200Microservice.Services.Interfaces;

namespace Sage200Microservice.API.Controllers
{
    /// <summary>Admin + public endpoints for managing and validating API keys.</summary>
    [ApiController]
    [Route("api/apikeys")]
    [Produces("application/json")]
    public sealed class ApiKeysController : ControllerBase
    {
        private readonly ILogger<ApiKeysController> _logger;
        private readonly IApiKeyService _apiKeyService;

        public ApiKeysController(ILogger<ApiKeysController> logger, IApiKeyService apiKeyService)
        {
            _logger = logger;
            _apiKeyService = apiKeyService;
        }

        // =========================
        // Admin endpoints (CRUD)
        // =========================

        /// <summary>Gets all API keys.</summary>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<List<ApiKeyResponseDto>>> GetAllAsync(CancellationToken ct)
        {
            var apiKeys = await _apiKeyService.GetAllAsync();
            var list = apiKeys.Select(MapToResponseDto).ToList();
            return Ok(list);
        }

        /// <summary>Gets an API key by ID.</summary>
        [HttpGet("{id:int}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ApiKeyResponseDto>> GetByIdAsync(int id, CancellationToken ct)
        {
            var apiKey = await _apiKeyService.GetByIdAsync(id);
            if (apiKey is null) return NotFound();
            return Ok(MapToResponseDto(apiKey));
        }

        /// <summary>Creates a new API key.</summary>
        [HttpPost]
        [Consumes("application/json")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<ApiKeyResponseDto>> CreateAsync(
            [FromBody] CreateApiKeyRequestDto request,
            CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var created = await _apiKeyService.CreateAsync(
                request.ClientName,
                request.ExpiresAt,
                request.AllowedIpAddresses);

            var response = MapToResponseDto(created);
            return CreatedAtAction(nameof(GetByIdAsync), new { id = created.Id }, response);
        }

        /// <summary>Updates an API key.</summary>
        [HttpPut("{id:int}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ApiKeyResponseDto>> UpdateAsync(
            int id,
            [FromBody] UpdateApiKeyRequestDto request,
            CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var existing = await _apiKeyService.GetByIdAsync(id);
            if (existing is null) return NotFound();

            existing.ClientName = request.ClientName;
            existing.ExpiresAt = request.ExpiresAt;
            existing.IsActive = request.IsActive;
            existing.AllowedIpAddresses = request.AllowedIpAddresses;

            var updated = await _apiKeyService.UpdateAsync(existing);
            return Ok(MapToResponseDto(updated));
        }

        /// <summary>Deactivates an API key.</summary>
        [HttpPost("{id:int}/deactivate")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeactivateAsync(int id, CancellationToken ct)
        {
            var ok = await _apiKeyService.DeactivateAsync(id);
            return ok ? NoContent() : NotFound();
        }

        /// <summary>Rotates an API key, keeping the old one active for a grace period.</summary>
        [HttpPost("{id:int}/rotate")]
        [Consumes("application/json")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ApiKeyResponseDto>> RotateAsync(
            int id,
            [FromBody] RotateApiKeyRequestDto request,
            CancellationToken ct)
        {
            try
            {
                var rotated = await _apiKeyService.RotateAsync(id, request.GracePeriodDays);
                if (rotated is null) return NotFound();
                return Ok(MapToResponseDto(rotated));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rotating API key {Id}", id);
                return BadRequest(new { message = ex.Message });
            }
        }

        // =========================
        // Public validation
        // =========================

        /// <summary>Validates an API key and returns basic info.</summary>
        [HttpGet("validate/{key}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiKeyValidationResponseDto>> ValidateAsync(string key, CancellationToken ct)
        {
            var entity = await _apiKeyService.ValidateAsync(key);
            var isValid = entity is not null;

            // Optionally record usage (if service supports it)
            if (isValid)
            {
                try { await _apiKeyService.RecordUsageAsync(key); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to record API key usage for {Key}", key); }
            }

            var dto = new ApiKeyValidationResponseDto
            {
                IsValid = isValid,
                ClientName = entity?.ClientName,
                IsActive = entity?.IsActive ?? false,
                ExpiresAt = entity?.ExpiresAt,
                PreviousKeyExpiresAt = entity?.PreviousKeyExpiresAt,
                Version = entity?.Version ?? 0
            };

            return Ok(dto);
        }

        /// <summary>Lightweight validation (204 if valid, 404 if not).</summary>
        [HttpHead("validate/{key}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ValidateHeadAsync(string key, CancellationToken ct)
        {
            var entity = await _apiKeyService.ValidateAsync(key);
            return entity is null ? NotFound() : NoContent();
        }

        // =========================
        // Mapping
        // =========================
        private static ApiKeyResponseDto MapToResponseDto(ApiKey apiKey) => new()
        {
            Id = apiKey.Id,
            Key = apiKey.Key,
            ClientName = apiKey.ClientName,
            CreatedAt = apiKey.CreatedAt,
            ExpiresAt = apiKey.ExpiresAt,
            IsActive = apiKey.IsActive,
            LastUsedAt = apiKey.LastUsedAt,
            AllowedIpAddresses = apiKey.AllowedIpAddresses,
            Version = apiKey.Version,
            HasPreviousKey = !string.IsNullOrEmpty(apiKey.PreviousKey),
            PreviousKeyExpiresAt = apiKey.PreviousKeyExpiresAt
        };
    }

    // Small DTO used for /validate response (kept here to avoid coupling admin DTO)
    public sealed class ApiKeyValidationResponseDto
    {
        public bool IsValid { get; set; }
        public string? ClientName { get; set; }
        public bool IsActive { get; set; }
        public int Version { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime? PreviousKeyExpiresAt { get; set; }
    }
}
