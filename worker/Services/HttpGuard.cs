namespace Javideo.Worker.Services;

/// <summary>
/// Outbound-URL guard for URLs that come from external data (API responses).
/// Only http(s) to public DNS hosts is allowed — rejects non-http schemes,
/// raw-IP hosts, and localhost / loopback / private-suffix hostnames, so a
/// malicious subtitle entry can't make the worker probe internal addresses.
/// </summary>
public static class HttpGuard
{
    public static bool IsSafePublicUrl(string? url, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return false;
        // Raw-IP hosts are rejected outright (covers loopback/private/reserved
        // ranges); legitimate CDN hosts are DNS names.
        if (u.HostNameType != UriHostNameType.Dns) return false;
        var host = u.Host.ToLowerInvariant();
        if (host == "localhost" || !host.Contains('.')) return false;
        if (host.EndsWith(".local") || host.EndsWith(".internal") || host.EndsWith(".lan")) return false;
        uri = u;
        return true;
    }
}
