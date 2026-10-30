using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeeDPI.Core.Ipc;

/// <summary>
/// One line of newline-delimited JSON on the private control pipe between the
/// BEE DPI window and the elevated engine host.
/// </summary>
public sealed class HostMessage
{
    public string Type { get; set; } = string.Empty;

    public string? State { get; set; }

    public int? Pid { get; set; }

    public string? Code { get; set; }

    public string? Message { get; set; }

    /// <summary>
    /// Opaque JSON document exchanged with the elevated DNS helper. The UI never
    /// concatenates request content into a command line.
    /// </summary>
    public string? Payload { get; set; }
}

public static class HostProtocol
{
    public const string Start = "start";
    public const string Stop = "stop";
    public const string Shutdown = "shutdown";
    public const string Status = "status";
    public const string Dns = "dns";
    public const string DnsResult = "dns-result";

    public const string ReadyMessage = "ready";
    public const string Stopped = "stopped";
    public const string Event = "event";
    public const string Error = "error";

    public const string StateRunning = "running";
    public const string StateFaulted = "faulted";
    public const string StateStopped = "stopped";

    public const string DnsApplyAction = "apply";
    public const string DnsRestoreAction = "restore";
    public const string ModeEngine = "engine";
    public const string ModeDns = "dns";

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static Task SendAsync(StreamWriter writer, HostMessage message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        // Messages are single-line by contract.
        json = json.Replace('\r', ' ').Replace('\n', ' ');
        return writer.WriteLineAsync(json.AsMemory(), cancellationToken);
    }

    public static async Task<HostMessage?> ReceiveAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (line is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<HostMessage>(line, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static HostMessage Ready(int pid) => new()
    {
        Type = ReadyMessage,
        Pid = pid
    };

    public static HostMessage StoppedMessage() => new()
    {
        Type = Stopped
    };

    public static HostMessage Running(int pid) => new()
    {
        Type = Event,
        State = StateRunning,
        Pid = pid
    };

    public static HostMessage Faulted(string message) => new()
    {
        Type = Event,
        State = StateFaulted,
        Message = message
    };

    public static HostMessage ErrorMessage(string code, string message) => new()
    {
        Type = Error,
        Code = code,
        Message = message
    };

    public static HostMessage DnsRequest(string action, string payload) => new()
    {
        Type = Dns,
        Code = action,
        Payload = payload
    };

    public static HostMessage DnsResponse(string payload) => new()
    {
        Type = DnsResult,
        Payload = payload
    };
}
