using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sage200Microservice.Services.Interfaces
{
    /// <summary>
    /// Back-compat extension so legacy call sites that use
    /// ExchangeAuthorizationCodeAsync still compile. It delegates
    /// to the new ExchangeCodeForTokensAsync and throws if that fails.
    /// </summary>
    public static class SageAuthServiceCompatExtensions
    {
        /// <summary>
        /// Exchanges the authorization code for tokens (compat name).
        /// </summary>
        public static async Task ExchangeAuthorizationCodeAsync(
            this ISageAuthenticationService svc,
            string code,
            CancellationToken ct = default)
        {
            var (ok, error) = await svc.ExchangeCodeForTokensAsync(code, ct);
            if (!ok)
                throw new InvalidOperationException($"OAuth exchange failed: {error}");
        }
    }
}
