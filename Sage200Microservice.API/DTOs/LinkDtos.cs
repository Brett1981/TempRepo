namespace Sage200Microservice.API.DTOs
{
    public sealed class LinkResolveResponseDto
    {
        public long? SageId { get; set; }
        public string? SageUrn { get; set; }
        public string? SageCode { get; set; }
    }

    public sealed class LinkReverseItemDto
    {
        public int AppId { get; set; }
        public string ExternalRef { get; set; } = string.Empty;
    }

    public sealed class LinkReverseResponseDto
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public List<LinkReverseItemDto> Items { get; set; } = new();
    }
}