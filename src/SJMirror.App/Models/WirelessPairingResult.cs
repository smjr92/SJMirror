namespace SJMirror.App.Models;

public sealed record WirelessPairingResult(
    bool IsSuccess,
    WirelessPairingState State,
    string? ErrorMessage = null)
{
    public static WirelessPairingResult Success { get; } =
        new(true, WirelessPairingState.Connected);

    public static WirelessPairingResult Failure(
        WirelessPairingState state,
        string message) => new(false, state, message);
}
