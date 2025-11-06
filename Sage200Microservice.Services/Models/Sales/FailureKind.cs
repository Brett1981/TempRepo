namespace Sage200Microservice.Services.Models.Sales
{
    /// <summary>
    /// Categorises creation failures so controllers can map status codes consistently.
    /// </summary>
    public enum FailureKind
    {
        None = 0,
        Validation = 1,   // Local validation (rare; usually handled by model binding)
        Upstream = 2,      // Upstream/Sage failure (HTTP, schema, URN missing, etc.)
        BadRequest = 3    // Explicit bad request (400) from upstream
    }
}