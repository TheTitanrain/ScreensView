using ScreensView.Viewer.Services;
using ScreensView.Viewer.Views;

namespace ScreensView.Tests;

public sealed class RemoteAgentInstallerTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "ScreensViewTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ResolveDeploymentPlan_Windows7Sp1WithNet48_UsesLegacyPayload()
    {
        var os = new RemoteOperatingSystemInfo(
            "Microsoft Windows 7 Professional",
            new Version(6, 1, 7601),
            "32-bit",
            1,
            1);

        var plan = RemoteAgentInstaller.ResolveDeploymentPlan(os, 528049);

        Assert.Equal(AgentPayloadKind.Legacy, plan.PayloadKind);
        Assert.Equal("ScreensView.Agent.Legacy.exe", plan.ServiceExecutableName);
    }

    [Fact]
    public void ResolveDeploymentPlan_Windows7WithoutNet48_ThrowsHelpfulError()
    {
        var os = new RemoteOperatingSystemInfo(
            "Microsoft Windows 7 Professional",
            new Version(6, 1, 7601),
            "32-bit",
            1,
            1);

        var ex = Assert.Throws<InvalidOperationException>(() => RemoteAgentInstaller.ResolveDeploymentPlan(os, 461808));

        Assert.Contains(".NET Framework 4.8", ex.Message);
    }

    [Fact]
    public void ResolveDeploymentPlan_Windows10_UsesModernPayload()
    {
        var os = new RemoteOperatingSystemInfo(
            "Microsoft Windows 10 Pro",
            new Version(10, 0, 19045),
            "64-bit",
            1,
            0);

        var plan = RemoteAgentInstaller.ResolveDeploymentPlan(os, null);

        Assert.Equal(AgentPayloadKind.Modern, plan.PayloadKind);
        Assert.Equal("ScreensView.Agent.exe", plan.ServiceExecutableName);
    }

    [Fact]
    public void ResolveDeploymentPlan_Windows81Workstation_ThrowsUnsupportedError()
    {
        var os = new RemoteOperatingSystemInfo(
            "Microsoft Windows 8.1 Pro",
            new Version(6, 3, 9600),
            "64-bit",
            1,
            0);

        var ex = Assert.Throws<InvalidOperationException>(() => RemoteAgentInstaller.ResolveDeploymentPlan(os, null));

        Assert.Contains("не поддерживается", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveDeploymentPlan_Server2012R2WithNet48_UsesLegacyPayload()
    {
        var os = new RemoteOperatingSystemInfo(
            "Microsoft Windows Server 2012 R2 Standard",
            new Version(6, 3, 9600),
            "64-bit",
            3,
            0);

        var plan = RemoteAgentInstaller.ResolveDeploymentPlan(os, 528049);

        Assert.Equal(AgentPayloadKind.Legacy, plan.PayloadKind);
        Assert.Equal("ScreensView.Agent.Legacy.exe", plan.ServiceExecutableName);
    }

    [Fact]
    public void ResolveDeploymentPlan_Server2012R2WithoutNet48_ThrowsHelpfulError()
    {
        var os = new RemoteOperatingSystemInfo(
            "Microsoft Windows Server 2012 R2 Standard",
            new Version(6, 3, 9600),
            "64-bit",
            3,
            0);

        var ex = Assert.Throws<InvalidOperationException>(() => RemoteAgentInstaller.ResolveDeploymentPlan(os, 461808));

        Assert.Contains(".NET Framework 4.8", ex.Message);
    }

    [Fact]
    public void ResolveDeploymentPlan_Server2016_UsesModernPayload()
    {
        var os = new RemoteOperatingSystemInfo(
            "Microsoft Windows Server 2016 Standard",
            new Version(10, 0, 14393),
            "64-bit",
            3,
            0);

        var plan = RemoteAgentInstaller.ResolveDeploymentPlan(os, null);

        Assert.Equal(AgentPayloadKind.Modern, plan.PayloadKind);
        Assert.Equal("ScreensView.Agent.exe", plan.ServiceExecutableName);
    }

    [Fact]
    public void ClassifyRuntimeInstallTarget_Windows10_RequiresRuntimeInstall()
    {
        var os = new RemoteOperatingSystemInfo(
            "Microsoft Windows 10 Pro",
            new Version(10, 0, 19045),
            "64-bit",
            1,
            0);

        var target = RemoteAgentInstaller.ClassifyRuntimeInstallTarget(os);

        Assert.Equal(RuntimeInstallTarget.ModernSupported, target);
    }

    [Fact]
    public void ClassifyRuntimeInstallTarget_Server2012R2_SkipsAsNotRequired()
    {
        var os = new RemoteOperatingSystemInfo(
            "Microsoft Windows Server 2012 R2 Standard",
            new Version(6, 3, 9600),
            "64-bit",
            3,
            0);

        var target = RemoteAgentInstaller.ClassifyRuntimeInstallTarget(os);

        Assert.Equal(RuntimeInstallTarget.LegacySupported, target);
    }

    [Fact]
    public void ClassifyRuntimeInstallTarget_Windows81_SkipsAsUnsupported()
    {
        var os = new RemoteOperatingSystemInfo(
            "Microsoft Windows 8.1 Pro",
            new Version(6, 3, 9600),
            "64-bit",
            1,
            0);

        var target = RemoteAgentInstaller.ClassifyRuntimeInstallTarget(os);

        Assert.Equal(RuntimeInstallTarget.Unsupported, target);
    }

    [Theory]
    [InlineData(0, RuntimeInstallStatus.Installed, AgentLogLevel.Success)]
    [InlineData(3010, RuntimeInstallStatus.InstalledRebootRequired, AgentLogLevel.Warning)]
    public void InterpretRuntimeInstallerExitCode_KnownCodes_ReturnExpectedOutcome(
        int exitCode,
        RuntimeInstallStatus expectedStatus,
        AgentLogLevel expectedLevel)
    {
        var outcome = RemoteAgentInstaller.InterpretRuntimeInstallerExitCode(exitCode);

        Assert.Equal(expectedStatus, outcome.Status);
        Assert.Equal(expectedLevel, outcome.Level);
        Assert.NotEmpty(outcome.Message);
    }

    [Fact]
    public void InterpretRuntimeInstallerExitCode_UnexpectedCode_ThrowsHelpfulError()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => RemoteAgentInstaller.InterpretRuntimeInstallerExitCode(1603));

        Assert.Contains("1603", ex.Message);
    }

    [Fact]
    public void BuildRuntimeCompletionMessage_RebootRequired_ExplainsNextStep()
    {
        var outcome = new RuntimeInstallOutcome(
            RuntimeInstallStatus.InstalledRebootRequired,
            "Требуется перезагрузка",
            AgentLogLevel.Warning);

        var message = InstallProgressWindow.BuildCompletionMessage(InstallProgressWindow.Mode.InstallDotNetRuntime, outcome);

        Assert.Contains("перезагрузка", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildServiceCommand_QuotesExecutablePath()
    {
        var command = RemoteAgentInstaller.BuildServiceCommand(AgentDeploymentPlan.Legacy);

        Assert.Equal("\"C:\\Windows\\ScreensViewAgent\\ScreensView.Agent.Legacy.exe\"", command);
    }

    [Fact]
    public void GetWindowTitle_InstallDotNetRuntime_ReturnsExpectedTitle()
    {
        var title = InstallProgressWindow.GetWindowTitle(InstallProgressWindow.Mode.InstallDotNetRuntime);

        Assert.Equal("Установка .NET 8 Runtime", title);
    }

    [Fact]
    public void CopyPayloadFiles_CopiesOnlyPayloadFiles()
    {
        var sourceDir = Path.Combine(_tempRoot, "payload");
        var targetDir = Path.Combine(_tempRoot, "target");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(targetDir);

        File.WriteAllText(Path.Combine(sourceDir, "ScreensView.Agent.Legacy.exe"), "exe");
        File.WriteAllText(Path.Combine(sourceDir, "ScreensView.Agent.Legacy.exe.config"), "config");
        File.WriteAllText(Path.Combine(sourceDir, "helper.dll"), "dll");
        File.WriteAllText(Path.Combine(sourceDir, "skip.pdb"), "symbols");
        File.WriteAllText(Path.Combine(sourceDir, "ScreensView.Viewer.dll"), "viewer");

        RemoteAgentInstaller.CopyPayloadFiles(sourceDir, targetDir);

        Assert.True(File.Exists(Path.Combine(targetDir, "ScreensView.Agent.Legacy.exe")));
        Assert.True(File.Exists(Path.Combine(targetDir, "ScreensView.Agent.Legacy.exe.config")));
        Assert.True(File.Exists(Path.Combine(targetDir, "helper.dll")));
        Assert.False(File.Exists(Path.Combine(targetDir, "skip.pdb")));
        Assert.False(File.Exists(Path.Combine(targetDir, "ScreensView.Viewer.dll")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
