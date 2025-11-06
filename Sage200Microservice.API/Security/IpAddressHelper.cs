using System.Net;

namespace Sage200Microservice.API.Security
{
    /// <summary>
    /// Helper class for IP address operations
    /// </summary>
    public static class IpAddressHelper
    {
        /// <summary>
        /// Checks if an IP address is within a CIDR range
        /// </summary>
        /// <param name="ipAddress">    The IP address to check </param>
        /// <param name="cidrNotation"> The CIDR notation (e.g., 192.168.1.0/24) </param>
        /// <returns> True if the IP address is within the CIDR range, false otherwise </returns>
        public static bool IsInCidrRange(IPAddress ipAddress, string cidrNotation)
        {
            if (ipAddress == null || string.IsNullOrWhiteSpace(cidrNotation))
            {
                return false;
            }

            // Parse CIDR notation
            var parts = cidrNotation.Split('/');
            if (parts.Length != 2)
            {
                return false;
            }

            if (!IPAddress.TryParse(parts[0], out var networkAddress))
            {
                return false;
            }

            if (!int.TryParse(parts[1], out var prefixLength))
            {
                return false;
            }

            // Handle IPv4 and IPv6 differently
            if (ipAddress.AddressFamily != networkAddress.AddressFamily)
            {
                return false;
            }

            var ipBytes = ipAddress.GetAddressBytes();
            var networkBytes = networkAddress.GetAddressBytes();

            if (ipBytes.Length != networkBytes.Length)
            {
                return false;
            }

            // Calculate the number of bytes and bits in the prefix
            var prefixFullBytes = prefixLength / 8;
            var prefixRemainingBits = prefixLength % 8;

            // Check full bytes
            for (var i = 0; i < prefixFullBytes; i++)
            {
                if (ipBytes[i] != networkBytes[i])
                {
                    return false;
                }
            }

            // Check remaining bits
            if (prefixRemainingBits > 0 && prefixFullBytes < ipBytes.Length)
            {
                var mask = (byte)(0xFF << (8 - prefixRemainingBits));
                if ((ipBytes[prefixFullBytes] & mask) != (networkBytes[prefixFullBytes] & mask))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Checks if an IP address matches any of the specified CIDR ranges or IP addresses
        /// </summary>
        /// <param name="ipAddress"> The IP address to check </param>
        /// <param name="ranges">    The list of CIDR ranges or IP addresses </param>
        /// <returns> True if the IP address matches any of the ranges, false otherwise </returns>
        public static bool IsInRanges(IPAddress ipAddress, IEnumerable<string> ranges)
        {
            if (ipAddress == null || ranges == null)
            {
                return false;
            }

            foreach (var range in ranges)
            {
                // Check if it's a CIDR range
                if (range.Contains('/'))
                {
                    if (IsInCidrRange(ipAddress, range))
                    {
                        return true;
                    }
                }
                // Check if it's a direct IP match
                else if (IPAddress.TryParse(range, out var rangeIp) && ipAddress.Equals(rangeIp))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Parses an IP address from a string, handling IPv4, IPv6, and potential port numbers
        /// </summary>
        /// <param name="ipString"> The IP address string </param>
        /// <returns> The parsed IP address, or null if parsing fails </returns>
        public static IPAddress ParseIpAddress(string ipString)
        {
            if (string.IsNullOrWhiteSpace(ipString))
            {
                return null;
            }

            // Handle IPv6 with port (e.g., [::1]:8080)
            if (ipString.StartsWith("[") && ipString.Contains("]:"))
            {
                var endBracketIndex = ipString.IndexOf(']');
                if (endBracketIndex > 0)
                {
                    ipString = ipString.Substring(1, endBracketIndex - 1);
                }
            }
            // Handle IPv4 with port (e.g., 127.0.0.1:8080)
            else if (ipString.Contains(':') && ipString.Count(c => c == ':') == 1)
            {
                ipString = ipString.Split(':')[0];
            }

            // Try to parse the IP address
            if (IPAddress.TryParse(ipString, out var ipAddress))
            {
                return ipAddress;
            }

            return null;
        }

        /// <summary>
        /// Extracts the client IP address from the request, considering X-Forwarded-For if configured
        /// </summary>
        /// <param name="remoteIpAddress">    The remote IP address from the connection </param>
        /// <param name="forwardedFor">       The X-Forwarded-For header value </param>
        /// <param name="trustXForwardedFor"> Whether to trust the X-Forwarded-For header </param>
        /// <returns> The client IP address </returns>
        public static IPAddress GetClientIpAddress(IPAddress remoteIpAddress, string forwardedFor, bool trustXForwardedFor)
        {
            if (trustXForwardedFor && !string.IsNullOrWhiteSpace(forwardedFor))
            {
                // X-Forwarded-For can contain multiple IPs, the first one is the client
                var ips = forwardedFor.Split(',');
                if (ips.Length > 0)
                {
                    var clientIp = ParseIpAddress(ips[0].Trim());
                    if (clientIp != null)
                    {
                        return clientIp;
                    }
                }
            }

            return remoteIpAddress;
        }

        /// <summary>
        /// Validates a list of IP addresses or CIDR ranges
        /// </summary>
        /// <param name="ipRanges"> The list of IP addresses or CIDR ranges </param>
        /// <returns> True if all entries are valid, false otherwise </returns>
        public static bool ValidateIpRanges(IEnumerable<string> ipRanges)
        {
            if (ipRanges == null)
            {
                return true;
            }

            foreach (var range in ipRanges)
            {
                // Check if it's a CIDR range
                if (range.Contains('/'))
                {
                    var parts = range.Split('/');
                    if (parts.Length != 2)
                    {
                        return false;
                    }

                    if (!IPAddress.TryParse(parts[0], out _))
                    {
                        return false;
                    }

                    if (!int.TryParse(parts[1], out var prefixLength))
                    {
                        return false;
                    }

                    // Validate prefix length
                    if (prefixLength < 0 || prefixLength > 128)
                    {
                        return false;
                    }
                }
                // Check if it's a direct IP
                else if (!IPAddress.TryParse(range, out _))
                {
                    return false;
                }
            }

            return true;
        }
    }
}