using System.Globalization;
using BeeDPI.Core.Ipc;

namespace BeeDPI.EngineHost;

internal sealed class HostOptions
{
    public required string PipeName { get; init; }

    public required int ParentProcessId { get; init; }

    public string Mode { get; init; } = HostProtocol.ModeEngine;

    public static HostOptions? Parse(string[] args)
    {
        string? pipe = null;
        int parent = 0;
        var mode = HostProtocol.ModeEngine;

        for (var i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--pipe":
                    pipe = args[i + 1];
                    i++;
                    break;
                case "--parent":
                    int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out parent);
                    i++;
                    break;
                case "--mode":
                    mode = (args[i + 1] ?? string.Empty).Trim().ToLowerInvariant();
                    i++;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(pipe) || pipe.Length > 200 || parent <= 0)
        {
            return null;
        }

        if (mode is not (HostProtocol.ModeEngine or HostProtocol.ModeDns))
        {
            return null;
        }

        foreach (var c in pipe)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-'))
            {
                return null;
            }
        }

        return new HostOptions
        {
            PipeName = pipe,
            ParentProcessId = parent,
            Mode = mode
        };
    }
}
