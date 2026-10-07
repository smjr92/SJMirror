using SJMirror.App.Models;

namespace SJMirror.App.Services;

public sealed class WirelessMdnsServicesParser
{
    public IReadOnlyList<WirelessMdnsService> Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var services = new List<WirelessMdnsService>();

        foreach (var line in output.Split(
                     ['\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("List of discovered", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var tokens = line.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length < 3)
            {
                continue;
            }

            var typeIndex = Array.FindIndex(
                tokens,
                token => token.Equals("_adb-tls-pairing._tcp", StringComparison.OrdinalIgnoreCase) ||
                         token.Equals("_adb-tls-connect._tcp", StringComparison.OrdinalIgnoreCase));
            if (typeIndex <= 0 || typeIndex >= tokens.Length - 1)
            {
                continue;
            }

            services.Add(new WirelessMdnsService(
                tokens[typeIndex - 1],
                tokens[typeIndex],
                tokens[typeIndex + 1]));
        }

        return services;
    }
}
