namespace ScreensView.Viewer.Services;

internal enum AgentPayloadKind
{
    Modern,
    Legacy
}

internal sealed record RemoteOperatingSystemInfo(
    string Caption,
    Version Version,
    string Architecture,
    int ProductType,
    int ServicePackMajorVersion)
{
    public bool IsWorkstation => ProductType == 1;

    public bool IsWindows7 => IsWorkstation && Version.Major == 6 && Version.Minor == 1;

    public bool IsWindows7Sp1OrLater => IsWindows7 && ServicePackMajorVersion >= 1;

    public bool IsSupportedModernClient => IsWorkstation && Version.Major >= 10;

    public bool IsLegacyServer =>
        !IsWorkstation && Version.Major == 6 && (Version.Minor == 2 || Version.Minor == 3);

    public bool IsSupportedModernServer => !IsWorkstation && Version.Major >= 10;
}

internal sealed record AgentDeploymentPlan(AgentPayloadKind PayloadKind, string PayloadFolderName, string ServiceExecutableName)
{
    public static AgentDeploymentPlan Modern { get; } = new(AgentPayloadKind.Modern, "Modern", "ScreensView.Agent.exe");

    public static AgentDeploymentPlan Legacy { get; } = new(AgentPayloadKind.Legacy, "Legacy", "ScreensView.Agent.Legacy.exe");
}
