using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace FeedbackRelay;

/// <summary>
/// Who is calling, as far as rate limiting and logs need to know - and no further.
///
/// The key is the <c>CF-Connecting-IP</c> header when present (cloudflared forwards it; the
/// Cloudflare edge overwrites any client-supplied value, and the relay only listens on
/// loopback, so nothing else can reach it), else the socket's remote address.
///
/// The raw IP never leaves this class: callers get an HMAC of it under a random key that
/// lives only in this process's memory. Logs therefore carry a correlatable-within-this-run
/// token that can't be brute-forced back to an address (an unkeyed hash of an IPv4 address
/// can be, in seconds). A restart re-keys it.
/// </summary>
public sealed class ClientIdentity
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    /// <summary>Full-length keyed hash: the rate-limiter key.</summary>
    public string KeyFor(HttpContext ctx)
    {
        var ip = ResolveIp(ctx);
        var mac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(ip));
        return Convert.ToHexString(mac).ToLowerInvariant();
    }

    /// <summary>The short form printed in log lines.</summary>
    public static string Short(string key) => key.Length > 12 ? key.Substring(0, 12) : key;

    private static string ResolveIp(HttpContext ctx)
    {
        var header = ctx.Request.Headers["CF-Connecting-IP"].ToString().Trim();
        if (header.Length > 0 && IPAddress.TryParse(header, out var fromHeader))
            return Normalize(fromHeader);

        var remote = ctx.Connection.RemoteIpAddress;
        return remote == null ? "unknown" : Normalize(remote);
    }

    /// <summary>IPv4-mapped IPv6 becomes plain IPv4; any other IPv6 address is keyed by its
    /// /64 - one subscriber typically holds a whole /64, so a per-address key would let them
    /// rotate through 2^64 "different" clients.</summary>
    private static string Normalize(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily != AddressFamily.InterNetworkV6) return ip.ToString();
        var bytes = ip.GetAddressBytes();
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant() + "::/64";
    }
}
