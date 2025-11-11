using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sage200Microservice.Services.Auth
{
    public interface IOAuthStateStore
    {
        Task<string> CreateAsync(TimeSpan ttl, CancellationToken ct = default);
        Task<bool> TryConsumeAsync(string state, CancellationToken ct = default);
    }
}
