using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sage200Microservice.API.Protos.Sop;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Shared;
using DomainSop = Sage200Microservice.Services.Models.Sop;
// Namespace aliases to disambiguate proto vs domain models with the same short names
using ProtoSop = Sage200Microservice.API.Protos.Sop;

namespace Sage200Microservice.API.Services;

/// <summary>
/// gRPC façade for SOP orders, with HTTP/JSON transcoding mapped in sop.proto.
/// </summary>
[Authorize(Policy = "ApiUser")]
[ApiExplorerSettings(IgnoreApi = true)] // Hide controller from Swagger to avoid conflict with gRPC JSON-transcoding
public sealed class SopGrpcService : SopService.SopServiceBase
{
    private readonly ISopOrderService _svc;
    private readonly ISopOrderStatusService _statusSvc;
    private readonly ISopDocumentStatusTypeService _typesSvc;
    private readonly ILogger<SopGrpcService> _log;

    public SopGrpcService(
        ISopOrderService svc,
        ISopOrderStatusService statusSvc,
        ISopDocumentStatusTypeService typesSvc,
        ILogger<SopGrpcService> log)
    {
        _svc = svc;
        _statusSvc = statusSvc;
        _typesSvc = typesSvc;
        _log = log;
    }

    /// <summary>
    /// List SOP orders (header-only).
    /// </summary>
    public override async Task<ListSopOrdersResponse> ListSopOrders(ListSopOrdersRequest request, ServerCallContext context)
    {
        var q = new DomainSop.SopOrderQuery
        {
            ODataFilter = request.Filter,
            OrderBy = request.OrderBy,
            Top = request.Top,
            Skip = request.Skip,
            CustomerId = request.CustomerId == 0 ? null : request.CustomerId,
            OrderNo = string.IsNullOrWhiteSpace(request.OrderNo) ? null : request.OrderNo,
            Status = string.IsNullOrWhiteSpace(request.Status) ? null : request.Status,
            FromDate = DateTime.TryParse(request.FromDate, out var fd) ? fd : null,
            ToDate = DateTime.TryParse(request.ToDate, out var td) ? td : null
        };

        var page = await _svc.ListAsync(q, context.GetHttpContext(), context.CancellationToken);
        var rsp = new ListSopOrdersResponse { Total = page.TotalCount };

        foreach (var item in page.Items)
        {
            rsp.Items.Add(new SopOrder
            {
                Id = (int)item.Id,
                OrderNo = item.OrderNo ?? "",
                CustomerId = (int)item.CustomerId,
                CustomerReference = item.CustomerReference ?? "",
                Status = item.Status ?? "",
                OrderDate = item.OrderDate?.ToString("O") ?? "",
                PromisedDate = item.PromisedDate?.ToString("O") ?? "",
                CurrencyCode = item.CurrencyCode ?? "",
                NetTotal = (double)(item.NetTotal ?? 0),
                TaxTotal = (double)(item.TaxTotal ?? 0),
                GrossTotal = (double)(item.GrossTotal ?? 0),
                SourceExternalId = item.SourceExternalId ?? ""
            });
        }
        return rsp;
    }

    public override async Task<UpdateSopOrderStatusResponse> UpdateSopOrderStatus(UpdateSopOrderStatusRequest request, ServerCallContext context)
    {
        if (request.Id <= 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "id must be > 0"));

        if (string.IsNullOrWhiteSpace(request.Status))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "status is required"));

        // Validate mapping early to fail-fast on invalid terms
        try { StatusMapping.ToSageEnum(request.Status); }
        catch (Exception ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }

        var result = await _statusSvc.UpdateStatusAsync(new DomainSop.SopOrderStatusUpdate
        {
            OrderId = request.Id,
            Status = request.Status,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason
        }, context.GetHttpContext(), context.CancellationToken);

        return new UpdateSopOrderStatusResponse
        {
            Success = result.Success,
            Message = result.Message ?? "",
            OrderId = (int)result.OrderId,
            NewStatus = result.NewStatus ?? ""
        };
    }

    /// <summary>
    /// Lists "Outstanding" SOP orders using WHITELIST:
    /// Live(0), On hold(1), Disputed(3), Draft(5), Printed(6).
    /// We also omit $count to avoid backend 5xx patterns.
    /// </summary>
    public override async Task<ListSopOrdersResponse> ListOutstandingSopOrders(OutstandingSopOrdersRequest request, ServerCallContext context)
    {
        var injectedFilter = FriendlyFilters.AppendOutstandingWhitelist(request.Filter);

        var q = new DomainSop.SopOrderQuery
        {
            ODataFilter = injectedFilter,
            OrderBy = request.OrderBy,
            Top = request.Top,
            Skip = request.Skip,
            CustomerId = request.CustomerId == 0 ? null : request.CustomerId,
            OrderNo = string.IsNullOrWhiteSpace(request.OrderNo) ? null : request.OrderNo,
            Status = null,
            FromDate = DateTime.TryParse(request.FromDate, out var fd) ? fd : null,
            ToDate = DateTime.TryParse(request.ToDate, out var td) ? td : null,
            IncludeCount = false,
            StatusWhitelist = new[] { 0, 1, 3, 5, 6 }
        };

        var page = await _svc.ListAsync(q, context.GetHttpContext(), context.CancellationToken);
        var rsp = new ListSopOrdersResponse { Total = page.TotalCount };
        foreach (var item in page.Items)
        {
            rsp.Items.Add(new SopOrder
            {
                Id = (int)item.Id,
                OrderNo = item.OrderNo ?? "",
                CustomerId = (int)item.CustomerId,
                CustomerReference = item.CustomerReference ?? "",
                Status = item.Status ?? "",
                OrderDate = item.OrderDate?.ToString("O") ?? "",
                PromisedDate = item.PromisedDate?.ToString("O") ?? "",
                CurrencyCode = item.CurrencyCode ?? "",
                NetTotal = (double)(item.NetTotal ?? 0),
                TaxTotal = (double)(item.TaxTotal ?? 0),
                GrossTotal = (double)(item.GrossTotal ?? 0),
                SourceExternalId = item.SourceExternalId ?? ""
            });
        }
        return rsp;
    }

    /// <summary>
    /// Lists SOP Document Status Types (lookup).
    /// </summary>
    public override async Task<ListSopDocumentStatusTypesResponse> ListSopDocumentStatusTypes(ListSopDocumentStatusTypesRequest request, ServerCallContext context)
    {
        var items = await _typesSvc.ListAsync(context.GetHttpContext(), context.CancellationToken);
        var rsp = new ListSopDocumentStatusTypesResponse();
        foreach (var i in items)
        {
            rsp.Items.Add(new SopDocumentStatusType
            {
                Code = i.Code ?? string.Empty,
                Name = i.Name ?? string.Empty,
                Description = i.Description ?? string.Empty
            });
        }
        return rsp;
    }


    /// <summary>
    /// Get SOP order with lines.
    /// </summary>
    public override async Task<SopOrder> GetSopOrder(GetSopOrderRequest request, ServerCallContext context)
    {
        var dto = await _svc.GetAsync(request.Id, context.GetHttpContext(), context.CancellationToken)
                  ?? throw new RpcException(new Status(StatusCode.NotFound, "Order not found"));

        var rsp = new SopOrder
        {
            Id = (int)dto.Id,
            OrderNo = dto.OrderNo ?? "",
            CustomerId = (int)dto.CustomerId,
            CustomerReference = dto.CustomerReference ?? "",
            Status = dto.Status ?? "",
            OrderDate = dto.OrderDate?.ToString("O") ?? "",
            PromisedDate = dto.PromisedDate?.ToString("O") ?? "",
            CurrencyCode = dto.CurrencyCode ?? "",
            NetTotal = (double)(dto.NetTotal ?? 0),
            TaxTotal = (double)(dto.TaxTotal ?? 0),
            GrossTotal = (double)(dto.GrossTotal ?? 0),
            SourceExternalId = dto.SourceExternalId ?? ""
        };

        foreach (var l in dto.Lines ?? Enumerable.Empty<DomainSop.SopOrderLineDto>())
        {
            rsp.Lines.Add(new SopOrderLine
            {
                OrderId = (int)l.OrderId,
                LineNumber = l.LineNumber,
                ProductCode = l.ProductCode ?? "",
                Description = l.Description ?? "",
                Quantity = (double)l.Quantity,
                UnitPrice = (double)l.UnitPrice,
                NetTotal = (double)l.NetTotal,
                TaxTotal = (double)l.TaxTotal,
                GrossTotal = (double)l.GrossTotal,
                SourceExternalLineId = l.SourceExternalLineId ?? ""
            });
        }

        return rsp;
    }

    /// <summary>
    /// Create SOP order (idempotent).
    /// </summary>
    public override async Task<CreateSopOrderResponse> CreateSopOrder(
    CreateSopOrderRequest request,
    ServerCallContext context)
    {
        var httpContext = context.GetHttpContext();
        var ct = context.CancellationToken;

        // Map proto -> domain using fully-qualified domain types to remove ambiguity.
        var model = new DomainSop.SopOrderCreate
        {
            Header = new DomainSop.SopOrderCreateHeader
            {
                CustomerId = request.Header?.CustomerId ?? 0,
                CustomerReference = request.Header?.CustomerReference?.Trim(),
                PromisedDate = TryParseDate(request.Header?.PromisedDate),
                CurrencyCode = request.Header?.CurrencyCode?.Trim(),
                SourceExternalId = request.Header?.SourceExternalId?.Trim()
            },

            // request.Lines is RepeatedField<ProtoSop.SopOrderCreateLine> and is never null.
            Lines = request.Lines
                .Select(l => new DomainSop.SopOrderCreateLine
                {
                    ProductCode = l.ProductCode?.Trim(),
                    Quantity = ConvertToDecimal(l.Quantity),
                    UnitPrice = ConvertToDecimal(l.UnitPrice),
                    Description = l.Description?.Trim(),
                    SourceExternalLineId = l.SourceExternalLineId?.Trim()
                })
                .ToList(),

            IdempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
                ? null
                : request.IdempotencyKey.Trim()
        };

        if (model.Header.CustomerId <= 0)
        {
            return new CreateSopOrderResponse
            {
                Success = false,
                Message = "CustomerId must be specified (> 0).",
                OrderId = 0,
                OrderReference = string.Empty
            };
        }

        var result = await _svc.CreateAsync(model, httpContext, ct);

        return new CreateSopOrderResponse
        {
            Success = result.Success,
            Message = result.Message ?? string.Empty,
            OrderId = (int)(result.OrderId ?? 0),
            OrderReference = result.OrderReference ?? string.Empty
        };
    }

    private static ListSopOrdersResponse MapPage(Sage200Microservice.Services.Models.PagedResult<DomainSop.SopOrderDto> page)
    {
        var rsp = new ListSopOrdersResponse { Total = page.TotalCount };
        foreach (var item in page.Items)
        {
            rsp.Items.Add(new SopOrder
            {
                Id = (int)item.Id,
                OrderNo = item.OrderNo ?? "",
                CustomerId = (int)item.CustomerId,
                CustomerReference = item.CustomerReference ?? "",
                Status = item.Status ?? "",
                OrderDate = item.OrderDate?.ToString("O") ?? "",
                PromisedDate = item.PromisedDate?.ToString("O") ?? "",
                CurrencyCode = item.CurrencyCode ?? "",
                NetTotal = (double)(item.NetTotal ?? 0),
                TaxTotal = (double)(item.TaxTotal ?? 0),
                GrossTotal = (double)(item.GrossTotal ?? 0),
                SourceExternalId = item.SourceExternalId ?? ""
            });
        }
        return rsp;
    }

    /// <summary>
    /// Parses an ISO-8601 (or common) date string into <see cref="DateTime?"/> (local kind not enforced).
    /// </summary>
    private static DateTime? TryParseDate(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        return DateTime.TryParse(input, out var dt) ? dt : null;
    }

    /// <summary>
    /// Converts a double to decimal deterministically for money/quantity fields.
    /// </summary>
    private static decimal ConvertToDecimal(double value) => Convert.ToDecimal(value);

}