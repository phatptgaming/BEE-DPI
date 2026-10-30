using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;

namespace BeeDPI.Core.Connectivity;

/// <summary>
/// Result of contacting one public Steam endpoint.
/// A DNS failure, TLS failure or timeout means the endpoint did not answer.
/// Any HTTP status code (including 403, 429 and 5xx) proves that a real HTTP
/// response was received, so it is never treated as evidence of DPI blocking.
/// </summary>
public enum SteamProbeOutcome
{
    Reachable = 0,
    ServerError = 1,
    Timeout = 2,
    TlsFailure = 3,
    DnsFailure = 4,
    NetworkFailure = 5
}

public sealed record SteamEndpoint(string Name, string Url);

public sealed record SteamEndpointResult(
    string Name,
    string Url,
    SteamProbeOutcome Outcome,
    int? StatusCode,
    string Detail)
{
    public bool IsReachable => Outcome is SteamProbeOutcome.Reachable or SteamProbeOutcome.ServerError;
}

/// <summary>
/// Aggregated connectivity result. Reachable means at least one Steam endpoint
/// returned a real HTTP response. No credentials are ever sent.
/// </summary>
public sealed record SteamProbeResult(IReadOnlyList<SteamEndpointResult> Endpoints)
{
    public bool Reachable => Endpoints.Any(endpoint => endpoint.IsReachable);

    public bool Degraded =>
        Reachable && Endpoints.Where(endpoint => endpoint.IsReachable)
            .All(endpoint => endpoint.Outcome == SteamProbeOutcome.ServerError);

    public string Summary => Reachable
        ? Degraded
            ? "Steam có phản hồi nhưng máy chủ đang tạm thời lỗi."
            : "Steam phản hồi bình thường."
        : "Không nhận được phản hồi từ Steam.";

    public string Detail =>
        Summary + " " + string.Join(
            " · ",
            Endpoints.Select(endpoint => endpoint.Name + ": " + endpoint.Detail));
}

/// <summary>
/// Light-weight, read-only Steam reachability probe.
///
/// It performs plain HTTPS GET requests to public Steam pages, follows redirects
/// and reads only the response headers. It never posts a form, never logs in and
/// never collects credentials.
/// </summary>
public sealed class SteamProbe : IDisposable
{
    public const string UserAgent = "BEE-DPI/1.2.0 (+steam-connectivity-check)";

    public static readonly IReadOnlyList<SteamEndpoint> DefaultEndpoints = new[]
    {
        new SteamEndpoint("Steam Store", "https://store.steampowered.com/"),
        new SteamEndpoint("Steam Login", "https://store.steampowered.com/login/?redir=account%2F&redir_ssl=1"),
        new SteamEndpoint("Steam Community", "https://steamcommunity.com/login/home/")
    };

    private readonly HttpClient _client;
    private readonly TimeSpan _timeout;

    public SteamProbe(HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(8);

        var messageHandler = handler ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(6),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        };

        // The handler lifetime is owned by the caller (tests) or by this probe.
        _client = new HttpClient(messageHandler, disposeHandler: handler is null)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        _client.DefaultRequestHeaders.TryAddWithoutValidation(
            "Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
    }

    public async Task<SteamProbeResult> RunAsync(
        IReadOnlyList<SteamEndpoint>? endpoints = null,
        CancellationToken cancellationToken = default)
    {
        var targets = endpoints ?? DefaultEndpoints;
        var probes = targets.Select(target => ProbeAsync(target, cancellationToken)).ToArray();
        var results = await Task.WhenAll(probes).ConfigureAwait(false);
        return new SteamProbeResult(results);
    }

    public void Dispose() => _client.Dispose();

    private async Task<SteamEndpointResult> ProbeAsync(SteamEndpoint endpoint, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.Url);
            using var response = await _client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);

            stopwatch.Stop();
            var status = (int)response.StatusCode;
            var outcome = status >= 500 ? SteamProbeOutcome.ServerError : SteamProbeOutcome.Reachable;

            return new SteamEndpointResult(
                endpoint.Name,
                endpoint.Url,
                outcome,
                status,
                "HTTP " + status + " (" + stopwatch.ElapsedMilliseconds + " ms)");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SteamEndpointResult(
                endpoint.Name,
                endpoint.Url,
                SteamProbeOutcome.Timeout,
                null,
                "hết thời gian chờ");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            return ClassifyHttpFailure(endpoint, exception);
        }
        catch (Exception exception)
        {
            return new SteamEndpointResult(
                endpoint.Name,
                endpoint.Url,
                SteamProbeOutcome.NetworkFailure,
                null,
                Safe(exception.Message));
        }
    }

    private static SteamEndpointResult ClassifyHttpFailure(SteamEndpoint endpoint, HttpRequestException exception)
    {
        var outcome = exception.HttpRequestError switch
        {
            HttpRequestError.NameResolutionError => SteamProbeOutcome.DnsFailure,
            HttpRequestError.SecureConnectionError => SteamProbeOutcome.TlsFailure,
            _ => exception.InnerException switch
            {
                AuthenticationException => SteamProbeOutcome.TlsFailure,
                SocketException socket when socket.SocketErrorCode is
                    SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain =>
                    SteamProbeOutcome.DnsFailure,
                _ => SteamProbeOutcome.NetworkFailure
            }
        };

        var detail = outcome switch
        {
            SteamProbeOutcome.DnsFailure => "không phân giải được DNS",
            SteamProbeOutcome.TlsFailure => "lỗi TLS: " + Safe(exception.InnerException?.Message ?? exception.Message),
            _ => "lỗi mạng: " + Safe(exception.Message)
        };

        return new SteamEndpointResult(endpoint.Name, endpoint.Url, outcome, null, detail);
    }

    private static string Safe(string value)
    {
        var single = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return single.Length <= 160 ? single : single[..160];
    }
}

/// <summary>
/// The exact status labels required by the product specification. The ordinary
/// user only sees whether BEE DPI is connected; Steam probe details stay in the
/// application log.
/// </summary>
public static class ConnectionStatusText
{
    public const string Connecting = "ĐANG KẾT NỐI...";
    public const string EngineRunning = Connected;
    public const string Connected = "ĐÃ KẾT NỐI";
    public const string SteamUnresponsive = Connected;
    public const string Failed = "KHÔNG THỂ KẾT NỐI";
    public const string Disconnecting = "ĐANG HỦY KẾT NỐI...";
    public const string Idle = "CHƯA KẾT NỐI";
    public const string Disconnected = Idle;
}
