using System.IO;

namespace ScreensView.Viewer.Services;

internal sealed class ViewerStartupOptions
{
    public bool IsValid { get; init; } = true;
    public string? ErrorMessage { get; init; }
    public string? ConnectionsFilePath { get; init; }
    public bool HasConnectionsFileOverride => !string.IsNullOrWhiteSpace(ConnectionsFilePath);
}

internal static class ViewerStartupOptionsParser
{
    private const string ConnectionsFileArgument = "--connections-file";
    private const string ConnectionsFileArgumentWithEqualsPrefix = "--connections-file=";

    public static ViewerStartupOptions Parse(IReadOnlyList<string> args)
    {
        string? connectionsFilePath = null;

        for (var index = 1; index < args.Count; index++)
        {
            var argument = args[index];
            if (argument.StartsWith(ConnectionsFileArgumentWithEqualsPrefix, StringComparison.Ordinal))
            {
                return Invalid("Use --connections-file <path>; the --connections-file=<path> form is not supported.");
            }

            if (!string.Equals(argument, ConnectionsFileArgument, StringComparison.Ordinal))
                continue;

            if (connectionsFilePath is not null)
            {
                return Invalid("Multiple --connections-file arguments are not allowed.");
            }

            if (index + 1 >= args.Count)
            {
                return Invalid("The --connections-file argument requires a path value.");
            }

            var candidate = args[++index];
            if (!Path.IsPathFullyQualified(candidate))
            {
                return Invalid("The --connections-file path must be an absolute or UNC path.");
            }

            connectionsFilePath = candidate;
        }

        return new ViewerStartupOptions
        {
            ConnectionsFilePath = connectionsFilePath
        };
    }

    private static ViewerStartupOptions Invalid(string message)
    {
        return new ViewerStartupOptions
        {
            IsValid = false,
            ErrorMessage = message
        };
    }
}
