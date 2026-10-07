namespace SJMirror.App.Models;

public enum WirelessPairingState
{
    Idle,
    Preparing,
    WaitingForScan,
    Pairing,
    Connecting,
    Connected,
    Failed,
    Cancelled,
    TimedOut
}
