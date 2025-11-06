namespace Sage200Microservice.Data.Models
{
    public class ApiLog
    {
        public int Id { get; set; }
        public string Endpoint { get; set; }
        public string RequestMethod { get; set; }
        public string RequestPayload { get; set; }
        public string ResponsePayload { get; set; }
        public int HttpStatusCode { get; set; }
        public DateTime Timestamp { get; set; }
        public string CallerId { get; set; }
        public string ApiType { get; set; } // "REST", "gRPC"
    }
}