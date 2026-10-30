namespace BeeDPI.Core;

/// <summary>
/// High-level states exposed by the application. The UI maps these to the short
/// Vietnamese labels required by the product specification.
/// </summary>
public enum EngineConnectionState
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Disconnecting = 3,
    DisconnectedJustNow = 4,
    Error = 5
}
