namespace Sage200Microservice.Services.Models.Sales
{
    /// <summary>
    /// External reference mapping item for link persistence.
    /// </summary>
    public sealed class ExternalRefItem
    {
        public int? AppId { get; set; }
        public string ExternalRef { get; set; } = string.Empty;
    }
}