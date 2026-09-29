using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
namespace HealthChecker;

public class UrlPolicy(IConfiguration config, IHostEnvironment environment)
{
    public static Uri Parse(string? value)
    {
        if (value is null || value.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Enter an HTTP or HTTPS URL without credentials (maximum 2048 characters).");
        return uri;
    }
    public bool IsDemo(Uri uri) => environment.IsDevelopment() && config.GetValue<bool>("AllowDemoTarget")
        && uri.Host == "demo-target" && uri.Port == 8080 && uri.Scheme == "http";
    public async Task<IPAddress[]> Resolve(Uri uri, CancellationToken ct)
    {
        var addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, ct);
        if (addresses.Length == 0 || (!IsDemo(uri) && addresses.Any(IsPrivate)))
            throw new ArgumentException("Private, local, and reserved network destinations are not allowed.");
        return addresses;
    }
    public static bool IsPrivate(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return b[0] is 0 or 10 or 127 || b[0] >= 224 || (b[0] == 169 && b[1] == 254)
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168)
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) || (b[0] == 198 && b[1] is 18 or 19)
                || (b[0] == 192 && b[1] == 0) || (b[0] == 198 && b[1] == 51 && b[2] == 100)
                || (b[0] == 203 && b[1] == 0 && b[2] == 113);
        // Only ordinary global IPv6 unicast; exclude transition/documentation ranges.
        return (b[0] & 0xe0) != 0x20 || (b[0] == 0x20 && b[1] == 0x02)
            || (b[0] == 0x20 && b[1] == 0x01 && (b[2] < 2 || (b[2] == 0x0d && b[3] == 0xb8)));
    }
}
public interface IWebsiteChecker { Task<CheckResult> Check(string url, CancellationToken ct); }
public class WebsiteChecker(UrlPolicy policy) : IWebsiteChecker
{
    public static bool IsHealthy(int status) => status is >= 200 and <= 299;
    public async Task<CheckResult> Check(string url, CancellationToken ct)
    {
        var result = new CheckResult();
        var timer = Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var uri = UrlPolicy.Parse(url);
            for (var redirects = 0; ; redirects++)
            {
                var addresses = await policy.Resolve(uri, deadline.Token);
                // Connect only to the validated DNS snapshot, preventing DNS rebinding.
                using var handler = new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    UseProxy = false,
                    ConnectCallback = async (context, token) =>
                    {
                        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                        try { await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, token); return new NetworkStream(socket, ownsSocket: true); }
                        catch { socket.Dispose(); throw; }
                    }
                };
                using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.UserAgent.ParseAdd("WebsiteHealthShowcase/1.0");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                var code = (int)response.StatusCode;
                result.StatusCode = code;
                if (code is 301 or 302 or 303 or 307 or 308 && response.Headers.Location is { } location)
                {
                    if (redirects >= 5) { result.Error = "Too many redirects"; break; }
                    uri = UrlPolicy.Parse(new Uri(uri, location).AbsoluteUri);
                    continue;
                }
                result.Healthy = IsHealthy(code);
                if (!result.Healthy) result.Error = $"HTTP {code}";
                break;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { result.Error = "Timed out after 10 seconds"; }
        catch (ArgumentException) { result.Error = "Destination blocked by URL policy"; }
        catch (Exception e) when (e is HttpRequestException or SocketException) { result.Error = "Connection failed"; }
        result.ResponseTimeMs = timer.ElapsedMilliseconds;
        return result;
    }
}
