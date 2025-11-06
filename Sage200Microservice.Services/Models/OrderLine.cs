namespace Sage200Microservice.Services.Models
{
    public class OrderLine
    {
        public string? ProductCode { get; set; } = "";
        public string? Description { get; set; } = "";
        public decimal Quantity { get; set; } = 0;
        public decimal UnitPrice { get; set; } = 0.00m;
    }
}