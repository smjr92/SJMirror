namespace SJMirror.App.Models;

public sealed record AndroidDevice(
    string Serial,
    AndroidDeviceState State,
    string? Manufacturer,
    string? Model,
    string? AndroidVersion,
    AndroidConnectionType ConnectionType);
