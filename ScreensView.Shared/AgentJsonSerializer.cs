using System.Globalization;
using System.Text;
using ScreensView.Shared.Models;

namespace ScreensView.Shared;

public static class AgentJsonSerializer
{
    public static string SerializeScreenshotResponse(ScreenshotResponse response)
    {
        return "{" +
               Quote("ImageBase64") + ":" + Quote(response.ImageBase64) + "," +
               Quote("Timestamp") + ":" + Quote(response.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)) + "," +
               Quote("MachineName") + ":" + Quote(response.MachineName) +
               "}";
    }

    public static string SerializeHealthResponse(string machineName)
    {
        return "{" +
               Quote("status") + ":" + Quote("ok") + "," +
               Quote("machine") + ":" + Quote(machineName) +
               "}";
    }

    private static string Quote(string? value) => "\"" + Escape(value ?? string.Empty) + "\"";

    private static string Escape(string value)
    {
        var sb = new StringBuilder(value.Length + 8);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (char.IsControl(ch))
                    {
                        sb.Append("\\u");
                        sb.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                    break;
            }
        }

        return sb.ToString();
    }
}
