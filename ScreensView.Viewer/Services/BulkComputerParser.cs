using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using ScreensView.Shared.Models;

namespace ScreensView.Viewer.Services;

public static class BulkComputerParser
{
    public static IReadOnlyList<ComputerConfig> ParseHosts(
        string text, int port, ISet<string> existingHosts)
    {
        var seen = new HashSet<string>(existingHosts, StringComparer.OrdinalIgnoreCase);
        var result = new List<ComputerConfig>();

        foreach (var line in text.Split('\n'))
        {
            var host = line.TrimEnd('\r').Trim();
            if (string.IsNullOrEmpty(host)) continue;
            if (!seen.Add(host)) continue;

            result.Add(MakeConfig(host, port));
        }

        return result;
    }

    public static IReadOnlyList<ComputerConfig> ParseIpRange(
        string startText, string endText, int port, ISet<string> existingHosts,
        out string? error)
    {
        error = null;

        if (!IPAddress.TryParse(startText.Trim(), out var startIp) ||
            !IPAddress.TryParse(endText.Trim(), out var endIp))
        {
            error = "Некорректный IP-адрес";
            return [];
        }

        if (startIp.AddressFamily != AddressFamily.InterNetwork ||
            endIp.AddressFamily != AddressFamily.InterNetwork)
        {
            error = "Поддерживается только IPv4";
            return [];
        }

        var startUint = ToUInt(startIp);
        var endUint = ToUInt(endIp);

        if (endUint < startUint)
        {
            error = "Конечный IP меньше начального";
            return [];
        }

        if (endUint - startUint >= 255)
        {
            error = "Диапазон не должен превышать 255 адресов";
            return [];
        }

        var seen = new HashSet<string>(existingHosts, StringComparer.OrdinalIgnoreCase);
        var result = new List<ComputerConfig>();

        for (var u = startUint; u <= endUint; u++)
        {
            var host = ToIPAddress(u).ToString();
            if (!seen.Add(host)) continue;
            result.Add(MakeConfig(host, port));
        }

        return result;
    }

    public static string GenerateApiKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexString(bytes).ToLower();
    }

    private static ComputerConfig MakeConfig(string host, int port) => new()
    {
        Name = host,
        Host = host,
        Port = port,
        ApiKey = GenerateApiKey(),
        IsEnabled = true
    };

    private static uint ToUInt(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        return (uint)bytes[0] << 24 | (uint)bytes[1] << 16 |
               (uint)bytes[2] << 8  | bytes[3];
    }

    private static IPAddress ToIPAddress(uint u) =>
        new(new byte[] { (byte)(u >> 24), (byte)(u >> 16), (byte)(u >> 8), (byte)u });
}
