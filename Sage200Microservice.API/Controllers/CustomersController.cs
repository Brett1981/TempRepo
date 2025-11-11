// Sage200/Sage200Microservice.API/Controllers/CustomersController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sage200Microservice.API.Attributes;
using Sage200Microservice.API.Controllers.Infrastructure;
using Sage200Microservice.API.DTOs;
using Sage200Microservice.API.Models.Customers;
using Sage200Microservice.API.Validators;
using Sage200Microservice.Data;
using Sage200Microservice.Data.Models;
using Sage200Microservice.Data.Repositories;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models.Customers;
using Sage200Microservice.Services.Models.Sage;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sage200Microservice.API.Controllers
{
    /// <summary>
    /// Customer endpoints (list, get by code, create). Bulk exports live in CustomerViewExportController.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public partial class CustomersController : SageRouteControllerBase
    {
        private readonly ISageApiClient _sage;
        private readonly ILogger<CustomersController> _log;
        private readonly ICustomerService _customerService;
        private readonly ApplicationContext _db;
        private readonly IExternalIdLinkRepository _links;
        private readonly IApiKeyRepository _apiKeys;

        public CustomersController(
            ISageApiClient sage,
            ILogger<CustomersController> log,
            ICustomerService customerService,
            ApplicationContext db,
            IExternalIdLinkRepository links,
            IApiKeyRepository apiKeys)
            : base(sage, log) // <-- IMPORTANT: call the SageRouteControllerBase ctor
        {
            _sage = sage;
            _log = log;
            _customerService = customerService;
            _db = db;
            _links = links;
            _apiKeys = apiKeys;
        }

        /// <summary> List customers using OData. Only $filter, $top, $skip, $orderby, $count are
        /// sent to Sage. You can also pass a friendly 'q' (free-text) which we convert to a
        /// supported filter. Strategy:
        /// 1) Try: contains(name,'{q}') or contains(customer_reference,'{q}')
        /// 2) On 4xx/5xx from Sage, fallback to: substringof('{q}',name) or substringof('{q}',customer_reference)
        /// Examples: GET /api/customers?top=25&skip=0&orderby=name asc GET
        /// /api/customers?filter=customer_reference eq 'CUST-001'&count=true GET
        /// /api/customers?q=ltd&top=50 </summary>
        [HttpGet]
        [ProducesResponseType(typeof(JsonElement), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ProblemDetails), (int)HttpStatusCode.BadGateway)]
        public async Task<IActionResult> GetAsync(
    [FromQuery] int top = 100,
    [FromQuery] int skip = 0,
    [FromQuery] string? filter = null,
    [FromQuery] string? orderBy = null,
    [FromQuery] bool? count = null,
    [FromQuery] string? q = null,
    CancellationToken ct = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            // Ensure X-Site & X-Company are available (inbound headers -> ambient -> /sites fallback)
            await EnsureRoutingAsync(cts.Token);

            if (top <= 0) top = 100;
            if (top > 100) top = 100;
            if (skip < 0) skip = 0;

            // -------- build filter (q → OData, else validate $filter) --------
            string? finalFilter = null;
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = EscapeODataString(q.Trim());
                finalFilter = $"contains(name,'{term}') or contains(reference,'{term}')";
            }
            else if (!string.IsNullOrWhiteSpace(filter))
            {
                var looksLikeOData =
                    filter.Contains("contains(", StringComparison.OrdinalIgnoreCase) ||
                    Regex.IsMatch(filter, @"\b(eq|ne|gt|ge|lt|le)\b", RegexOptions.IgnoreCase);
                if (!looksLikeOData)
                    return BadRequest(new { error = "Invalid $filter. Use 'q' or a valid OData filter (e.g. contains(name,'ltd'))." });

                // 🔧 normalize aliases & types so Sage accepts it
                finalFilter = NormalizeCustomersFilter(filter);
            }

            List<string> BuildParts(int thisTop, int thisSkip, string? thisFilter, string? thisOrderBy, bool? thisCount)
            {
                var parts = new List<string> { $"$top={thisTop}" };
                if (thisSkip > 0) parts.Add($"$skip={thisSkip}");
                if (!string.IsNullOrWhiteSpace(thisFilter)) parts.Add($"$filter={thisFilter}");
                if (!string.IsNullOrWhiteSpace(thisOrderBy)) parts.Add($"$orderby={thisOrderBy}");
                if (thisCount.HasValue) parts.Add($"$count={(thisCount.Value ? "true" : "false")}");
                return parts;
            }

            async Task<JsonElement> CallAsync(string root, int thisTop, int thisSkip, string? thisFilter, string? thisOrderBy, bool? thisCount)
            {
                var qs = string.Join("&", BuildParts(thisTop, thisSkip, thisFilter, thisOrderBy, thisCount));
                return await _sage.GetAsync<JsonElement>($"{root}?{qs}", cts.Token);
            }

            try
            {
                var data = await CallAsync("customers", top, skip, finalFilter, orderBy, count);
                return Ok(data);
            }
            // NEW: bubble a clear 400 when Sage rejects the filter (bad field/op/type)
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                var pd = new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Invalid OData query",
                    Detail = "Sage rejected the OData parameters. Check field names, operators, and literal types.",
                    Instance = HttpContext.Request.Path
                };
                // Helpful context for clients:
                pd.Extensions["filter"] = finalFilter ?? filter;
                pd.Extensions["orderBy"] = orderBy;
                pd.Extensions["top"] = top;
                pd.Extensions["skip"] = skip;
                pd.Extensions["upstream"] = ex.Message;
                return BadRequest(pd);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.BadGateway)
            {
                try
                {
                    var data = await CallAsync("customers", top, skip, finalFilter, string.IsNullOrWhiteSpace(orderBy) ? "name asc" : orderBy, count);
                    return Ok(data);
                }
                catch (HttpRequestException)
                {
                    try
                    {
                        var data = await CallAsync("customers", top, skip, finalFilter, string.IsNullOrWhiteSpace(orderBy) ? "name asc" : orderBy, true);
                        return Ok(data);
                    }
                    catch (HttpRequestException)
                    {
                        try
                        {
                            var data = await CallAsync("customers", Math.Min(50, top), skip, finalFilter, "name asc", true);
                            return Ok(data);
                        }
                        catch (HttpRequestException)
                        {
                            try
                            {
                                var data = await CallAsync("customer_views", Math.Min(50, top), skip, finalFilter, "name asc", true);
                                return Ok(data);
                            }
                            catch (HttpRequestException)
                            {
                                try
                                {
                                    var data = await CallAsync("customer_views", Math.Min(50, top), skip, finalFilter, "reference asc", true);
                                    return Ok(data);
                                }
                                catch (HttpRequestException)
                                {
                                    var correlationId = HttpContext.Items.ContainsKey("CorrelationId")
                                        ? HttpContext.Items["CorrelationId"]?.ToString()
                                        : HttpContext.TraceIdentifier;

                                    _log.LogInformation("Customers list degraded to empty page due to upstream gateway errors. CorrelationId={CorrelationId}", correlationId);

                                    var empty = new Dictionary<string, object>
                                    {
                                        ["value"] = Array.Empty<object>(),
                                        ["@odata.count"] = 0
                                    };
                                    return Ok(empty);
                                }
                            }
                        }
                    }
                }
            }
            catch (TaskCanceledException)
            {
                _log.LogWarning("Degraded /api/customers to 200(empty) due to timeout.");
                var empty = new { value = Array.Empty<object>(), count = 0 };
                return Ok(empty);
            }
        }

        /// <summary>
        /// Convenience: exact match on customer_reference (Top 1).
        /// </summary>
        [HttpGet("by-ref/{reference}")]
        [ProducesResponseType(typeof(JsonElement), (int)HttpStatusCode.OK)]
        public async Task<IActionResult> GetByReference([FromRoute] string reference, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            await EnsureRoutingAsync(cts.Token); // <-- important

            var eq = EscapeOData(reference);

            try
            {
                var custDet = await TryFirstOkAsync(cts.Token, new[]
                {
                    $"customers?$filter=reference eq '{eq}'&$top=1",
                    $"customers?$filter=code eq '{eq}'&$top=1",
                    $"customers?$filter=customer_reference eq '{eq}'&$top=1",
                    $"customers?$filter=contains(name,'{eq}')&$top=1"
                });

                var cust = MaterializeSingle<SageCustomer>(custDet!);
                if (cust == null)
                {
                    var pd = new ProblemDetails
                    {
                        Title = "Customer not found",
                        Detail = $"Customer '{reference}' not found.",
                        Status = StatusCodes.Status404NotFound,
                        Instance = HttpContext.Request.Path
                    };
                    pd.Extensions["traceId"] = HttpContext.Items.ContainsKey("CorrelationId")
                        ? HttpContext.Items["CorrelationId"]?.ToString()
                        : HttpContext.TraceIdentifier;
                    return NotFound(pd);
                }

                return Ok(custDet);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is null || (int)ex.StatusCode >= 500)
            {
                _log.LogWarning("Degraded /api/customers/by-ref to 200(empty) due to upstream {Status}.", (int?)ex.StatusCode);
                return Ok(new { });
            }
            catch (TaskCanceledException)
            {
                _log.LogWarning("Degraded /api/customers/by-ref to 200(empty) due to timeout.");
                return Ok(new { });
            }
        }

        /// <summary>
        /// Create a new customer in Sage 200 (and persist locally).
        /// Accepts optional externalRefs[] to create cross-app → Sage mappings after a successful Sage create.
        /// Now forwards HttpContext to the service to apply headers/idempotency.
        /// </summary>
        [HttpPost]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(CreateCustomerResult), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(CreateCustomerResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        //[SageRoutingHeaders(RequiresIdempotencyKey = true)]
        public async Task<IActionResult> CreateAsync([FromBody] CreateCustomerRequest request, CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            try
            {
                await EnsureRoutingAsync(ct); // <-- important
                // Convert to local entity
                var customer = new Sage200Microservice.Data.Models.Customer
                {
                    CustomerName = request.CustomerName,
                    CustomerCode = request.CustomerCode,
                    AddressLine1 = request.AddressLine1 ?? "",
                    AddressLine2 = request.AddressLine2 ?? "",
                    City = request.City ?? "",
                    Postcode = request.Postcode ?? "",
                    Telephone = request.Telephone ?? "",
                    Email = request.Email ?? "",
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = Request.Headers["caller-id"].FirstOrDefault() ?? "Unknown"
                };

                // PATCH: pass HttpContext to service
                var (ok, msg, localId, localCode) = await _customerService.CreateCustomerAsync(customer, HttpContext, ct);
                var payload = new CreateCustomerResult
                {
                    Success = ok,
                    Message = msg,
                    CustomerId = localId,
                    CustomerCode = localCode
                };

                if (!ok)
                    return BadRequest(payload);

                // Resolve AppId (if any external refs are present and no explicit AppId provided)
                int? headerAppId = null;
                if (request.ExternalRefs != null && request.ExternalRefs.Count > 0)
                {
                    var apiKey = Request.Headers["X-Api-Key"].ToString();
                    if (!string.IsNullOrWhiteSpace(apiKey))
                    {
                        // resolve AppId using existing repo methods (GetByKeyAsync / GetByPreviousKeyAsync + IsValidKeyAsync)
                        var keyRow = await _apiKeys.GetByKeyAsync(apiKey) ?? await _apiKeys.GetByPreviousKeyAsync(apiKey);
                        var valid = await _apiKeys.IsValidKeyAsync(apiKey);
                        if (keyRow == null || !valid)
                            return StatusCode(StatusCodes.Status401Unauthorized, new ProblemDetails
                            {
                                Type = "https://httpstatuses.com/401",
                                Title = "Unauthorized",
                                Status = StatusCodes.Status401Unauthorized,
                                Detail = "API key could not be resolved to a valid AppId."
                            });
                        // optional: update last used
                        await _apiKeys.UpdateLastUsedAsync(apiKey);
                        headerAppId = keyRow.Id;
                    }
                }

                await using var tx = await _db.Database.BeginTransactionAsync(ct);

                // Fetch the created Sage customer to obtain canonical ID (Helper Files indicate numeric Id)
                var created = await _customerService.GetCustomerByCodeAsync(request.CustomerCode, ct);
                var sageId = created?.Id;
                if (sageId == null)
                {
                    await tx.RollbackAsync(ct);
                    return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
                    {
                        Type = "https://httpstatuses.com/502",
                        Title = "Upstream error",
                        Status = StatusCodes.Status502BadGateway,
                        Detail = "Sage did not return a numeric customer Id when queried by customer code."
                    });
                }

                // 2) Persist ExternalIdLink(s) idempotently (no-op on replay)
                if (request.ExternalRefs != null)
                {
                    foreach (var item in request.ExternalRefs)
                    {
                        var appId = item.AppId ?? headerAppId;
                        if (appId == null)
                            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
                            {
                                Type = "https://httpstatuses.com/403",
                                Title = "Forbidden",
                                Status = StatusCodes.Status403Forbidden,
                                Detail = "AppId is required via item.appId or resolvable from 'X-Api-Key'."
                            });

                        try
                        {
                            await _links.TryInsertAsync(new ExternalIdLink
                            {
                                AppId = appId.Value,
                                EntityType = ExternalEntityType.Customer,
                                SageId = sageId,
                                SageUrn = null,
                                ExternalRef = item.ExternalRef
                            }, ct);
                        }
                        catch (InvalidOperationException ex)
                        {
                            var details = new ProblemDetails
                            {
                                Type = "https://httpstatuses.com/409",
                                Title = "External link conflict",
                                Status = StatusCodes.Status409Conflict,
                                Detail = ex.Message
                            };
                            details.Extensions["appId"] = appId.Value;
                            details.Extensions["entityType"] = ExternalEntityType.Customer.ToString();
                            details.Extensions["externalRef"] = item.ExternalRef;
                            details.Extensions["requestedSageId"] = sageId;
                            details.Extensions["requestedSageUrn"] = null;
                            await tx.RollbackAsync(ct);
                            return StatusCode(StatusCodes.Status409Conflict, details);
                        }
                    }
                }

                await tx.CommitAsync(ct);
                // Preserve existing behaviour (this endpoint previously returned OK with the created Sage payload)
                return Ok(created);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error creating customer {Code}", request.CustomerCode);
                return StatusCode(StatusCodes.Status500InternalServerError, new CreateCustomerResult
                {
                    Success = false,
                    Message = $"Error creating customer: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Composite details by customer code (reference).
        /// Also accepts a numeric <c>id</c> in the route and transparently resolves it to the customer reference.
        /// </summary>
        [HttpGet("{code}")]
        [ProducesResponseType(typeof(CustomerDetailsDto), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.NotFound)]
        public async Task<IActionResult> GetDetailsAsync(
            [FromRoute] string code,
            [FromQuery] GetCustomerDetailsQuery q,
            [FromServices] GetCustomerDetailsQueryValidator v,
            CancellationToken ct)
        {
            var vr = v.Validate(q);
            if (!vr.IsValid) return ValidationProblem(new ValidationProblemDetails(vr.ToDictionary()));

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            await EnsureRoutingAsync(cts.Token); // <-- important

            // If the route value looks like a numeric Id, resolve to reference first
            var originalRouteValue = code;
            if (IsAllDigits(code) && long.TryParse(code, out var id))
            {
                var resolvedRef = await TryResolveReferenceByIdAsync(id, cts.Token);
                if (!string.IsNullOrWhiteSpace(resolvedRef))
                {
                    code = resolvedRef!;
                }
                else
                {
                    var pd = new ProblemDetails
                    {
                        Title = "Customer not found",
                        Detail = $"Customer id '{originalRouteValue}' not found.",
                        Status = StatusCodes.Status404NotFound,
                        Instance = HttpContext.Request.Path
                    };
                    pd.Extensions["traceId"] = HttpContext.Items.ContainsKey("CorrelationId")
                        ? HttpContext.Items["CorrelationId"]?.ToString()
                        : HttpContext.TraceIdentifier;
                    return NotFound(pd);
                }
            }

            try
            {
                var details = await _customerService.GetCustomerDetailsAsync(code, q.Page, q.PageSize, cts.Token);
                var dto = Map(details);
                return Ok(dto);
            }
            catch (KeyNotFoundException)
            {
                var pd = new ProblemDetails
                {
                    Title = "Customer not found",
                    Detail = $"Customer '{originalRouteValue}' not found.",
                    Status = StatusCodes.Status404NotFound,
                    Instance = HttpContext.Request.Path
                };
                pd.Extensions["traceId"] = HttpContext.Items.ContainsKey("CorrelationId")
                    ? HttpContext.Items["CorrelationId"]?.ToString()
                    : HttpContext.TraceIdentifier;

                return NotFound(pd);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is null || (int)ex.StatusCode >= 500)
            {
                _log.LogWarning("Degraded /api/Customers/{Code} to 200(empty) due to upstream {Status}.", code, (int?)ex.StatusCode);
                return Ok(EmptyDetailsDto(code, q.Page, q.PageSize));
            }
            catch (TaskCanceledException)
            {
                _log.LogWarning("Degraded /api/Customers/{Code} to 200(empty) due to timeout.", code);
                return Ok(EmptyDetailsDto(code, q.Page, q.PageSize));
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                return BadRequest(new { message = "Bad request sent to Sage API. Check filter field names or customer code." });
            }
        }

        /// <summary>
        /// Resolve a customer's <c>reference</c> from its numeric <c>id</c>.
        /// Tries the canonical OData set first (<c>customers</c>), then falls back to
        /// <c>customer_views</c> and <c>lookup_customers</c> because Sage payloads can vary by tenant/version.
        /// Handles both OData shapes: { "value": [...] } and { "items": [...] }, and a single-object body.
        /// Returns <c>null</c> if nothing is found or upstream returns an error.
        /// </summary>
        private async Task<string?> TryResolveReferenceByIdAsync(long id, CancellationToken ct)
        {
            // Try most-specific to most-generic. Keep the query minimal for perf/stability.
            var candidates = new[]
            {
        $"customers?$filter=id eq {id}&$select=reference,code&$top=1",
        $"customer_views?$filter=id eq {id}&$select=reference&$top=1",
        $"lookup_customers?$filter=id eq {id}&$select=reference&$top=1"
    };
            await EnsureRoutingAsync(ct); // <-- important
            foreach (var path in candidates)
            {
                try
                {
                    using var doc = await _sage.GetAsync<JsonDocument>(path, ct);
                    var root = doc.RootElement;

                    // 1) Standard OData array shapes
                    if (TryGetFirstItem(root, out var first))
                    {
                        if (TryGetString(first, "reference", out var reference)) return reference;
                        if (TryGetString(first, "customer_reference", out reference)) return reference; // some views use this
                        if (TryGetString(first, "code", out reference)) return reference;              // last-resort: some payloads expose 'code'
                    }

                    // 2) Single-object shape (defensive)
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        if (TryGetString(root, "reference", out var directRef)) return directRef;
                        if (TryGetString(root, "customer_reference", out directRef)) return directRef;
                        if (TryGetString(root, "code", out directRef)) return directRef;
                    }

                    // 3) Raw top-level array (rare)
                    if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                    {
                        var firstEl = root[0];
                        if (TryGetString(firstEl, "reference", out var arrRef)) return arrRef;
                        if (TryGetString(firstEl, "customer_reference", out arrRef)) return arrRef;
                        if (TryGetString(firstEl, "code", out arrRef)) return arrRef;
                    }
                }
                catch (HttpRequestException)
                {
                    // swallow and try next candidate
                }
                catch (TaskCanceledException)
                {
                    // swallow and try next candidate
                }
            }

            return null;

            // -------- local helpers --------
            static bool TryGetFirstItem(JsonElement obj, out JsonElement first)
            {
                if (obj.ValueKind == JsonValueKind.Object)
                {
                    if (obj.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() > 0)
                    {
                        first = v[0];
                        return true;
                    }
                    if (obj.TryGetProperty("items", out var i) && i.ValueKind == JsonValueKind.Array && i.GetArrayLength() > 0)
                    {
                        first = i[0];
                        return true;
                    }
                }
                first = default;
                return false;
            }

            static bool TryGetString(JsonElement obj, string name, out string? value)
            {
                if (obj.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String)
                {
                    value = p.GetString();
                    return true;
                }
                value = null;
                return false;
            }
        }


        private static string EscapeOData(string s) => (s ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);

        private static T? MaterializeSingle<T>(JsonDocument doc)
        {
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("id", out _))
                    return JsonSerializer.Deserialize<T>(root.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (root.TryGetProperty("items", out var items) &&
                    items.ValueKind == JsonValueKind.Array &&
                    items.GetArrayLength() > 0)
                    return JsonSerializer.Deserialize<T>(items[0].GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }

            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                return JsonSerializer.Deserialize<T>(root[0].GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return default;
        }

        private static CustomerDetailsDto Map(CustomerDetails s) => new CustomerDetailsDto
        {
            CustomerReference = s.CustomerReference,
            Name = s.Name,
            Email = s.Email,
            Telephone = s.Telephone,
            Addresses = s.Addresses.Select(a => new CustomerAddressDto
            {
                Type = a.Type,
                Line1 = a.Line1,
                Line2 = a.Line2,
                City = a.City,
                Postcode = a.Postcode,
                Country = a.Country
            }).ToList(),
            RecentInvoices = new PagedResultDto<InvoiceSummaryDto>
            {
                Page = s.RecentInvoices.Page,
                PageSize = s.RecentInvoices.PageSize,
                TotalCount = s.RecentInvoices.TotalCount,
                Items = s.RecentInvoices.Items.Select(i => new InvoiceSummaryDto
                {
                    DocumentNo = i.DocumentNo,
                    OrderDateUtc = i.OrderDateUtc,
                    GrossValue = i.GrossValue,
                    OutstandingValue = i.OutstandingValue,
                    IsPaid = i.IsPaid,
                    Allocations = i.Allocations.Select(a => new AllocationSummaryDto
                    {
                        DocumentNo = i.DocumentNo,
                        TraderTransactionType = a.trader_transaction_type ?? "",
                        AllocationDateUtc = a.allocation_date,
                        Amount = (decimal)a.allocated_value
                    }).ToList()
                }).ToList()
            },
            OpenItemsCount = s.OpenItemsCount,
            OutstandingBalance = s.OutstandingBalance,
            RecentAllocations = s.RecentAllocations.Select(a => new AllocationSummaryDto
            {
                DocumentNo = a.DocumentNo,
                TraderTransactionType = a.TraderTransactionType,
                AllocationDateUtc = a.AllocationDateUtc,
                Amount = a.Amount
            }).ToList()
        };

        private static CustomerDetailsDto EmptyDetailsDto(string code, int page, int pageSize) => new CustomerDetailsDto
        {
            CustomerReference = code,
            Name = "",
            Email = "",
            Telephone = "",
            Addresses = new List<CustomerAddressDto>(),
            RecentInvoices = new PagedResultDto<InvoiceSummaryDto>
            {
                Page = page,
                PageSize = pageSize,
                TotalCount = 0,
                Items = new List<InvoiceSummaryDto>()
            },
            OpenItemsCount = 0,
            OutstandingBalance = 0m,
            RecentAllocations = new List<AllocationSummaryDto>()
        };

        private async Task<JsonDocument?> TryFirstOkAsync(CancellationToken ct, IEnumerable<string> candidates, bool swallowOnAllFail = false)
        {
            Exception? last = null;

            foreach (var path in candidates.Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    return await _sage.GetAsync<JsonDocument>(path, ct);
                }
                catch (HttpRequestException ex)
                {
                    last = ex;
                }
                catch (TaskCanceledException ex)
                {
                    last = ex;
                }
            }

            if (swallowOnAllFail) return null;
            throw last ?? new InvalidOperationException("All candidate endpoints failed.");
        }

        public sealed class CreateCustomerRequest
        {
            public string CustomerName { get; set; } = default!;
            public string CustomerCode { get; set; } = default!;
            public string? AddressLine1 { get; set; }
            public string? AddressLine2 { get; set; }
            public string? City { get; set; }
            public string? Postcode { get; set; }
            public string? Telephone { get; set; }
            public string? Email { get; set; }
            /// <summary>
            /// Optional external references that will be mapped to the created Sage customer.
            /// </summary>
            public List<ExternalRefItemDto>? ExternalRefs { get; set; }
        }

        public sealed class CreateCustomerResult
        {
            public bool Success { get; set; }
            public string Message { get; set; } = "";
            public long CustomerId { get; set; }
            public string CustomerCode { get; set; } = "";
        }

        private static string EscapeODataString(string value)
            => (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);

        private static bool LooksLikeValidFilter(string f)
        {
            if (string.IsNullOrWhiteSpace(f)) return false;
            var s = f.Trim();

            if (s.Contains(" contains(", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("contains(", StringComparison.OrdinalIgnoreCase) ||
                s.Contains(" substringof(", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("substringof(", StringComparison.OrdinalIgnoreCase))
                return true;

            var hasOp = Regex.IsMatch(s, @"\b(eq|ne|gt|ge|lt|le)\b", RegexOptions.IgnoreCase);
            return hasOp;
        }

        // PATCH 1: Normalize the incoming filter so aliases & types match the Sage "customers" OData model.
        // - Maps customer_id  -> id
        // - Maps customer_reference -> reference
        // - id comparisons:    id eq '123'  => id eq 123
        // - string fields:     reference eq 123 => reference eq '123'  (same for name)
        // - Leaves contains()/substringof() as-is
        private static string NormalizeCustomersFilter(string f)
        {
            if (string.IsNullOrWhiteSpace(f)) return f;
            var s = f.Trim();

            // Aliases → canonical field names used by the "customers" resource
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\bcustomer_id\b", "id");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\bcustomer_reference\b", "reference");

            // id: quoted numeric → numeric (id is Int64)
            s = System.Text.RegularExpressions.Regex.Replace(
                s,
                @"\bid\s+(eq|ne|gt|ge|lt|le)\s*'(\d+)'\b",
                m => $"id {m.Groups[1].Value} {m.Groups[2].Value}",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // reference/name: unquoted numeric literal → quoted (strings)
            s = System.Text.RegularExpressions.Regex.Replace(
                s,
                @"\b(reference|name)\s+(eq|ne)\s*(\d+)\b",
                m => $"{m.Groups[1].Value} {m.Groups[2].Value} '{m.Groups[3].Value}'",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            return s;
        }

        /// <summary>Returns true iff the string is non-empty and every character is a digit.</summary>
        private static bool IsAllDigits(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
                if (!char.IsDigit(s[i])) return false;
            return true;
        }
    }
}
