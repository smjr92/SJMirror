using SJMirror.App.Models;

namespace SJMirror.App.Services;

public sealed class AdbDevicesParser
{
    private const string DevicesHeader = "List of devices attached";

    public AdbDevicesParseResult Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var lines = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var headerIndex = Array.FindIndex(
            lines,
            line => line.StartsWith(DevicesHeader, StringComparison.OrdinalIgnoreCase));

        if (headerIndex < 0)
        {
            return new AdbDevicesParseResult(
                false,
                [],
                [],
                "The output from 'adb devices -l' did not contain the expected header.");
        }

        var devices = new List<AndroidDevice>();
        var warnings = new List<string>();

        foreach (var line in lines.Skip(headerIndex + 1))
        {
            var tokens = line.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length < 2)
            {
                warnings.Add($"Ignored unexpected ADB device line: '{line}'.");
                continue;
            }

            var serial = tokens[0];
            var state = ParseState(tokens[1]);
            var properties = ParseProperties(tokens.Skip(2));
            properties.TryGetValue("model", out var model);

            devices.Add(new AndroidDevice(
                serial,
                state,
                null,
                NormalizeListingValue(model),
                null,
                DetectConnectionType(serial)));
        }

        return new AdbDevicesParseResult(true, devices, warnings, null);
    }

    public static AndroidDeviceState ParseState(string state) =>
        state.ToLowerInvariant() switch
        {
            "device" => AndroidDeviceState.Device,
            "unauthorized" => AndroidDeviceState.Unauthorized,
            "offline" => AndroidDeviceState.Offline,
            "disconnected" => AndroidDeviceState.Disconnected,
            _ => AndroidDeviceState.Unknown
        };

    public static AndroidConnectionType DetectConnectionType(string serial)
    {
        if (serial.Contains(':', StringComparison.Ordinal) ||
            serial.Contains("_adb-tls-connect._tcp", StringComparison.OrdinalIgnoreCase))
        {
            return AndroidConnectionType.Network;
        }

        return serial.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase)
            ? AndroidConnectionType.Unknown
            : AndroidConnectionType.Usb;
    }

    private static Dictionary<string, string> ParseProperties(IEnumerable<string> tokens)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
        {
            var separatorIndex = token.IndexOf(':');
            if (separatorIndex <= 0 || separatorIndex == token.Length - 1)
            {
                continue;
            }

            properties[token[..separatorIndex]] = token[(separatorIndex + 1)..];
        }

        return properties;
    }

    private static string? NormalizeListingValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Replace('_', ' ');
}
