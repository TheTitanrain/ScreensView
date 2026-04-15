using System.IO;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using ScreensView.Shared;
using ScreensView.Shared.Models;

namespace ScreensView.Viewer.Services;

public enum RuntimeInstallTarget
{
    ModernSupported,
    LegacySupported,
    Unsupported
}

public enum RuntimeInstallStatus
{
    Installed,
    InstalledRebootRequired,
    SkippedNotRequired,
    SkippedUnsupported
}

public sealed record RuntimeInstallOutcome(
    RuntimeInstallStatus Status,
    string Message,
    AgentLogLevel Level);

internal enum RuntimeInstallerPackage
{
    DotNetRuntime,
    AspNetCoreRuntime
}

internal sealed record RuntimeInstallerSpec(
    RuntimeInstallerPackage Package,
    string DisplayName,
    string FileGlob);

internal sealed record RuntimeInstallStepResult(
    RuntimeInstallerPackage Package,
    RuntimeInstallOutcome Outcome);

public class RemoteAgentInstaller
{
    private const string PayloadsRootFolderName = "AgentPayloads";
    private const string PrerequisitesFolderName = "Prerequisites";
    // x64 only — modern agent EXE is PE machine 0x8664; x86 runtime cannot run it
    private const string DotNetRuntimeInstallerGlob = "dotnet-runtime-8.*-win-x64.exe";
    private const string AspNetCoreRuntimeInstallerGlob = "aspnetcore-runtime-8.*-win-x64.exe";
    private const int DotNetInstallerTimeoutSeconds = 300;
    private const uint HKeyLocalMachine = 0x80000002;
    private static readonly RuntimeInstallerSpec[] RequiredRuntimeInstallers =
    [
        new(RuntimeInstallerPackage.DotNetRuntime, ".NET Runtime", DotNetRuntimeInstallerGlob),
        new(RuntimeInstallerPackage.AspNetCoreRuntime, "ASP.NET Core Runtime", AspNetCoreRuntimeInstallerGlob)
    ];

    private readonly Action<string, string, string, AgentLogLevel> _log;
    private string? _username;
    private string? _password;

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(ref NETRESOURCE netResource, string? password, string? username, int flags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetCancelConnection2(string name, int flags, bool force);

    [StructLayout(LayoutKind.Sequential)]
    private struct NETRESOURCE
    {
        public int dwScope, dwType, dwDisplayType, dwUsage;
        public string? lpLocalName, lpRemoteName, lpComment, lpProvider;
    }

    public RemoteAgentInstaller(Action<string, string, string, AgentLogLevel> log)
    {
        _log = log;
    }

    public async Task InstallAsync(ComputerConfig computer, string? username = null, string? password = null)
    {
        _username = username;
        _password = password;
        await Task.Run(() =>
        {
            var plan = ResolveDeploymentPlanForHost(computer.Host);
            var unc = $@"\\{computer.Host}\Admin$";
            var targetDir = IsLoopback(computer.Host)
                ? Path.Combine(@"C:\Windows", Constants.AgentFolderName)
                : Path.Combine(unc, Constants.AgentFolderName);
            if (!IsLoopback(computer.Host))
                ConnectSmb(unc, username, password);
            try
            {
                Directory.CreateDirectory(targetDir);
                _log(computer.Name, "Остановка службы (если запущена)", string.Empty, AgentLogLevel.Info);
                try { StopService(computer); }
                catch (Exception ex) { _log(computer.Name, "Предупреждение: остановка службы", ex.Message, AgentLogLevel.Warning); }
                if (plan == AgentDeploymentPlan.Modern)
                    InstallRuntimePackages(computer, unc);
                _log(computer.Name, "Копирование файлов", string.Empty, AgentLogLevel.Info);
                CopyAgentFiles(targetDir, computer, plan);
                _log(computer.Name, "Создание службы", string.Empty, AgentLogLevel.Info);
                CreateService(computer, plan);
                _log(computer.Name, "Запуск службы", string.Empty, AgentLogLevel.Info);
                StartService(computer);
            }
            finally
            {
                if (!IsLoopback(computer.Host))
                    DisconnectSmb(unc);
            }
        });
    }

    public async Task UpdateAsync(ComputerConfig computer, string? username = null, string? password = null)
    {
        _username = username;
        _password = password;
        await Task.Run(() =>
        {
            var plan = ResolveDeploymentPlanForHost(computer.Host);
            var unc = $@"\\{computer.Host}\Admin$";
            var targetDir = IsLoopback(computer.Host)
                ? Path.Combine(@"C:\Windows", Constants.AgentFolderName)
                : Path.Combine(unc, Constants.AgentFolderName);
            if (!IsLoopback(computer.Host))
                ConnectSmb(unc, username, password);
            try
            {
                Directory.CreateDirectory(targetDir);
                _log(computer.Name, "Остановка службы", string.Empty, AgentLogLevel.Info);
                try { StopService(computer); }
                catch (Exception ex) { _log(computer.Name, "Предупреждение: остановка службы", ex.Message, AgentLogLevel.Warning); }
                if (plan == AgentDeploymentPlan.Modern)
                    InstallRuntimePackages(computer, unc);
                _log(computer.Name, "Обновление файлов", string.Empty, AgentLogLevel.Info);
                CopyAgentFiles(targetDir, computer, plan);
                _log(computer.Name, "Запуск службы", string.Empty, AgentLogLevel.Info);
                StartService(computer);
            }
            finally
            {
                if (!IsLoopback(computer.Host))
                    DisconnectSmb(unc);
            }
        });
    }

    public async Task<RuntimeInstallOutcome> InstallDotNetRuntimesAsync(
        ComputerConfig computer,
        string? username = null,
        string? password = null)
    {
        _username = username;
        _password = password;

        return await Task.Run(() =>
        {
            var os = GetRemoteOperatingSystemInfo(computer.Host);
            var target = ClassifyRuntimeInstallTarget(os);
            if (target != RuntimeInstallTarget.ModernSupported)
                return BuildRuntimeSkipOutcome(computer, os, target);

            var unc = $@"\\{computer.Host}\Admin$";
            if (!IsLoopback(computer.Host))
                ConnectSmb(unc, username, password);

            try
            {
                return InstallRuntimePackages(computer, unc);
            }
            finally
            {
                if (!IsLoopback(computer.Host))
                    DisconnectSmb(unc);
            }
        });
    }

    public async Task UninstallAsync(ComputerConfig computer, string? username = null, string? password = null)
    {
        _username = username;
        _password = password;
        await Task.Run(() =>
        {
            var unc = $@"\\{computer.Host}\Admin$";
            var targetDir = IsLoopback(computer.Host)
                ? Path.Combine(@"C:\Windows", Constants.AgentFolderName)
                : Path.Combine(unc, Constants.AgentFolderName);
            if (!IsLoopback(computer.Host))
                ConnectSmb(unc, username, password);
            try
            {
                _log(computer.Name, "Остановка службы", string.Empty, AgentLogLevel.Info);
                try { StopService(computer); }
                catch (Exception ex) { _log(computer.Name, "Предупреждение: остановка службы", ex.Message, AgentLogLevel.Warning); }
                _log(computer.Name, "Удаление службы", string.Empty, AgentLogLevel.Info);
                DeleteService(computer);
                if (Directory.Exists(targetDir))
                {
                    _log(computer.Name, "Удаление файлов", string.Empty, AgentLogLevel.Info);
                    Directory.Delete(targetDir, recursive: true);
                }
            }
            finally
            {
                if (!IsLoopback(computer.Host))
                    DisconnectSmb(unc);
            }
        });
    }

    internal static AgentDeploymentPlan ResolveDeploymentPlan(RemoteOperatingSystemInfo os, int? netFrameworkReleaseKey)
    {
        if (os.IsWindows7)
        {
            if (!os.IsWindows7Sp1OrLater)
                throw new InvalidOperationException("Windows 7 без Service Pack 1 не поддерживается для удаленной установки агента.");

            if (!HasNetFramework48OrNewer(netFrameworkReleaseKey))
                throw new InvalidOperationException("Для Windows 7 требуется установленный .NET Framework 4.8.");

            return AgentDeploymentPlan.Legacy;
        }

        if (os.IsLegacyServer)
        {
            if (!HasNetFramework48OrNewer(netFrameworkReleaseKey))
                throw new InvalidOperationException($"Для {os.Caption} требуется установленный .NET Framework 4.8.");

            return AgentDeploymentPlan.Legacy;
        }

        if (os.IsSupportedModernClient || os.IsSupportedModernServer)
            return AgentDeploymentPlan.Modern;

        throw new InvalidOperationException($"ОС '{os.Caption}' версии {os.Version} не поддерживается текущим агентом.");
    }

    internal static string BuildServiceCommand(AgentDeploymentPlan plan)
    {
        var executablePath = Path.Combine(@"C:\Windows", Constants.AgentFolderName, plan.ServiceExecutableName);
        return $"\"{executablePath}\"";
    }

    internal static void CopyPayloadFiles(string sourceDir, string targetDir)
    {
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException($"Agent payload directory '{sourceDir}' was not found.");

        CopyDirectoryRecursive(sourceDir, targetDir);
    }

    private static void CopyDirectoryRecursive(string sourceDir, string targetDir)
    {
        foreach (var file in Directory.GetFiles(sourceDir, "*.*"))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith("ScreensView.Viewer", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(file, Path.Combine(targetDir, name), overwrite: true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var subDirName = Path.GetFileName(subDir);
            if (subDirName.Equals("artifacts", StringComparison.OrdinalIgnoreCase)) continue;
            var targetSubDir = Path.Combine(targetDir, subDirName);
            if (!Directory.Exists(targetSubDir))
                Directory.CreateDirectory(targetSubDir);
            CopyDirectoryRecursive(subDir, targetSubDir);
        }
    }

    internal static bool HasNetFramework48OrNewer(int? releaseKey) => releaseKey >= 528040;

    internal static RuntimeInstallTarget ClassifyRuntimeInstallTarget(RemoteOperatingSystemInfo os)
    {
        if (os.IsWindows7 || os.IsLegacyServer)
            return RuntimeInstallTarget.LegacySupported;

        if (os.IsSupportedModernClient || os.IsSupportedModernServer)
            return RuntimeInstallTarget.ModernSupported;

        return RuntimeInstallTarget.Unsupported;
    }

    internal static IReadOnlyList<RuntimeInstallerSpec> GetRequiredRuntimeInstallers() => RequiredRuntimeInstallers;

    internal static string ResolveRuntimeInstallerPath(string prerequisitesDirectory, RuntimeInstallerPackage package)
    {
        if (!Directory.Exists(prerequisitesDirectory))
            throw new DirectoryNotFoundException(
                $"Каталог prerequisites '{prerequisitesDirectory}' не найден.");

        var installer = RequiredRuntimeInstallers.Single(candidate => candidate.Package == package);
        var installerPath = Directory
            .GetFiles(prerequisitesDirectory, installer.FileGlob)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (installerPath == null)
            throw new FileNotFoundException(
                $"Не найден офлайн-установщик {installer.DisplayName} в каталоге Prerequisites. " +
                $"Ожидается файл '{installer.FileGlob}'.",
                prerequisitesDirectory);

        return installerPath;
    }

    internal static RuntimeInstallOutcome InterpretRuntimeInstallerExitCode(int exitCode) => exitCode switch
    {
        0 => new RuntimeInstallOutcome(
            RuntimeInstallStatus.Installed,
            ".NET 8 Runtime установлен.",
            AgentLogLevel.Success),
        3010 => new RuntimeInstallOutcome(
            RuntimeInstallStatus.InstalledRebootRequired,
            ".NET 8 Runtime установлен, требуется перезагрузка компьютера.",
            AgentLogLevel.Warning),
        _ => throw new InvalidOperationException(
            $"Установщик .NET 8 завершился с кодом {exitCode}.")
    };

    internal static RuntimeInstallOutcome InterpretRuntimeInstallerExitCode(int exitCode, string packageDisplayName) => exitCode switch
    {
        0 => new RuntimeInstallOutcome(
            RuntimeInstallStatus.Installed,
            $"{packageDisplayName} установлен.",
            AgentLogLevel.Success),
        3010 => new RuntimeInstallOutcome(
            RuntimeInstallStatus.InstalledRebootRequired,
            $"{packageDisplayName} установлен, требуется перезагрузка компьютера.",
            AgentLogLevel.Warning),
        _ => throw new InvalidOperationException(
            $"Установщик {packageDisplayName} завершился с кодом {exitCode}.")
    };

    internal static RuntimeInstallOutcome CombineRuntimeInstallOutcomes(IReadOnlyList<RuntimeInstallStepResult> stepResults)
    {
        if (stepResults.Count == 0)
            throw new ArgumentException("Не переданы результаты установки runtime-пакетов.", nameof(stepResults));

        var rebootRequired = stepResults.Any(step => step.Outcome.Status == RuntimeInstallStatus.InstalledRebootRequired);
        return rebootRequired
            ? new RuntimeInstallOutcome(
                RuntimeInstallStatus.InstalledRebootRequired,
                ".NET 8 runtimes установлены, требуется перезагрузка компьютера.",
                AgentLogLevel.Warning)
            : new RuntimeInstallOutcome(
                RuntimeInstallStatus.Installed,
                ".NET 8 runtimes установлены.",
                AgentLogLevel.Success);
    }

    private static void CopyAgentFiles(string targetDir, ComputerConfig computer, AgentDeploymentPlan plan)
    {
        var sourceDir = GetPayloadSourceDirectory(plan);
        CopyPayloadFiles(sourceDir, targetDir);

        var settings = new
        {
            Logging = new { LogLevel = new { Default = "Information" } },
            Agent = new
            {
                Port = computer.Port,
                ApiKey = computer.ApiKey,
                ScreenshotQuality = 75
            }
        };
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(targetDir, "appsettings.json"), json);
    }

    private static string GetPayloadSourceDirectory(AgentDeploymentPlan plan)
    {
        var sourceRoot = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        return Path.Combine(sourceRoot, PayloadsRootFolderName, plan.PayloadFolderName);
    }

    private static string GetPrerequisitesSourceDirectory()
    {
        var sourceRoot = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        return Path.Combine(sourceRoot, PrerequisitesFolderName);
    }

    private AgentDeploymentPlan ResolveDeploymentPlanForHost(string host)
    {
        var os = GetRemoteOperatingSystemInfo(host);
        var releaseKey = (os.IsWindows7 || os.IsLegacyServer) ? TryGetNetFrameworkReleaseKey(host) : null;
        return ResolveDeploymentPlan(os, releaseKey);
    }

    private RemoteOperatingSystemInfo GetRemoteOperatingSystemInfo(string host)
    {
        var scope = WmiScope(host);
        var query = new ObjectQuery("SELECT Caption, Version, OSArchitecture, ProductType, ServicePackMajorVersion FROM Win32_OperatingSystem");
        using var searcher = new ManagementObjectSearcher(scope, query);
        using var os = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
        if (os == null)
            throw new InvalidOperationException($"Не удалось получить информацию об ОС для хоста '{host}'.");

        var versionText = Convert.ToString(os["Version"]);
        if (string.IsNullOrWhiteSpace(versionText))
            throw new InvalidOperationException($"WMI не вернул версию ОС для хоста '{host}'.");

        return new RemoteOperatingSystemInfo(
            Convert.ToString(os["Caption"]) ?? string.Empty,
            Version.Parse(versionText),
            Convert.ToString(os["OSArchitecture"]) ?? string.Empty,
            Convert.ToInt32(os["ProductType"] ?? 1),
            Convert.ToInt32(os["ServicePackMajorVersion"] ?? 0));
    }

    private int? TryGetNetFrameworkReleaseKey(string host)
    {
        using var reg = new ManagementClass(WmiScopeDefault(host), new ManagementPath("StdRegProv"), null);
        using var inParams = reg.GetMethodParameters("GetDWORDValue");
        inParams["hDefKey"] = HKeyLocalMachine;
        inParams["sSubKeyName"] = @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full";
        inParams["sValueName"] = "Release";

        using var outParams = reg.InvokeMethod("GetDWORDValue", inParams, null);
        if (outParams == null)
            return null;

        var returnValue = Convert.ToInt32(outParams["ReturnValue"] ?? 1);
        if (returnValue != 0 || outParams["uValue"] == null)
            return null;

        return Convert.ToInt32(outParams["uValue"]);
    }

    private RuntimeInstallOutcome BuildRuntimeSkipOutcome(
        ComputerConfig computer,
        RemoteOperatingSystemInfo os,
        RuntimeInstallTarget target)
    {
        var outcome = target switch
        {
            RuntimeInstallTarget.LegacySupported => new RuntimeInstallOutcome(
                RuntimeInstallStatus.SkippedNotRequired,
                $".NET 8 runtimes не требуются: для {os.Caption} используется legacy-агент.",
                AgentLogLevel.Info),
            RuntimeInstallTarget.Unsupported => new RuntimeInstallOutcome(
                RuntimeInstallStatus.SkippedUnsupported,
                $".NET 8 runtimes пропущены: ОС '{os.Caption}' не поддерживает modern-агент.",
                AgentLogLevel.Info),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null)
        };

        _log(computer.Name, "Установка .NET 8 runtimes не требуется", outcome.Message, outcome.Level);
        return outcome;
    }

    private RuntimeInstallOutcome InstallRuntimePackages(ComputerConfig computer, string unc)
    {
        var prerequisitesDirectory = GetPrerequisitesSourceDirectory();
        var stepResults = new List<RuntimeInstallStepResult>();

        foreach (var installer in RequiredRuntimeInstallers)
        {
            var localInstallerPath = ResolveRuntimeInstallerPath(prerequisitesDirectory, installer.Package);
            var outcome = InstallRuntimePackage(computer, unc, installer, localInstallerPath);
            stepResults.Add(new RuntimeInstallStepResult(installer.Package, outcome));
        }

        return CombineRuntimeInstallOutcomes(stepResults);
    }

    private RuntimeInstallOutcome InstallRuntimePackage(
        ComputerConfig computer,
        string unc,
        RuntimeInstallerSpec installer,
        string localInstallerPath)
    {
        var installerFileName = Path.GetFileName(localInstallerPath);
        var isLoopback = IsLoopback(computer.Host);
        var transferDirectory = isLoopback
            ? Path.Combine(@"C:\Windows", "Temp")
            : Path.Combine(unc, "Temp");
        var targetInstallerPath = Path.Combine(@"C:\Windows\Temp", installerFileName);
        var remoteInstallerPath = Path.Combine(transferDirectory, installerFileName);
        var markerFileName = $"dotnet-runtime-exit-{installer.Package}-{Guid.NewGuid():N}.txt";
        var targetMarkerPath = Path.Combine(@"C:\Windows\Temp", markerFileName);
        var remoteMarkerPath = Path.Combine(transferDirectory, markerFileName);

        _log(computer.Name, $"Установка {installer.DisplayName}", installerFileName, AgentLogLevel.Info);
        File.Copy(localInstallerPath, remoteInstallerPath, overwrite: true);

        try
        {
            var commandLine = BuildRuntimeInstallerCommandLine(targetInstallerPath, targetMarkerPath);
            _log(computer.Name, $"Запуск установщика {installer.DisplayName} (это может занять несколько минут)", string.Empty, AgentLogLevel.Info);
            var pid = RunProcessViaWmi(computer.Host, commandLine);

            _log(computer.Name, $"Ожидание завершения установки {installer.DisplayName}", $"PID: {pid}", AgentLogLevel.Info);
            var exitCode = WaitForRuntimeInstallerExitCode(remoteMarkerPath, pid, DotNetInstallerTimeoutSeconds);
            var outcome = InterpretRuntimeInstallerExitCode(exitCode, installer.DisplayName);

            _log(computer.Name, $"Установка {installer.DisplayName} завершена", outcome.Message, outcome.Level);
            return outcome;
        }
        finally
        {
            try { File.Delete(remoteInstallerPath); } catch { }
            try { File.Delete(remoteMarkerPath); } catch { }
        }
    }

    private static string BuildRuntimeInstallerCommandLine(string installerPath, string markerPath) =>
        $"cmd.exe /c \"\"{installerPath}\" /quiet /norestart & echo %ERRORLEVEL% > \"{markerPath}\"\"";

    private uint RunProcessViaWmi(string host, string commandLine)
    {
        using var processClass = new ManagementClass(WmiScope(host), new ManagementPath("Win32_Process"), null);
        using var inParams = processClass.GetMethodParameters("Create");
        inParams["CommandLine"] = commandLine;

        using var outParams = processClass.InvokeMethod("Create", inParams, null);
        if (outParams == null)
            throw new InvalidOperationException("Win32_Process.Create не вернул результат.");

        var returnValue = Convert.ToInt32(outParams["ReturnValue"]);
        if (returnValue != 0)
            throw new InvalidOperationException(
                $"Win32_Process.Create вернул код {returnValue}. " +
                "Убедитесь, что учётная запись имеет право на запуск процессов на удалённой машине.");

        return Convert.ToUInt32(outParams["ProcessId"]);
    }

    private static int WaitForRuntimeInstallerExitCode(string markerPath, uint processId, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (TryReadRuntimeInstallerExitCode(markerPath, out var exitCode))
                return exitCode;

            Thread.Sleep(5000);
        }

        throw new TimeoutException(
            $"Установщик .NET 8 (PID {processId}) не записал код завершения за {timeoutSeconds} секунд. " +
            "Проверьте состояние процесса на целевой машине вручную.");
    }

    private static bool TryReadRuntimeInstallerExitCode(string markerPath, out int exitCode)
    {
        exitCode = 0;
        if (!File.Exists(markerPath))
            return false;

        try
        {
            var rawValue = File.ReadAllText(markerPath).Trim();
            if (string.IsNullOrWhiteSpace(rawValue))
                return false;

            if (!int.TryParse(rawValue, out exitCode))
                throw new InvalidOperationException(
                    $"Файл результата установщика .NET содержит некорректное значение '{rawValue}'.");

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void CreateService(ComputerConfig computer, AgentDeploymentPlan plan)
    {
        var scope = WmiScope(computer.Host);
        var mc = new ManagementClass(scope, new ManagementPath("Win32_Service"), null);

        var inParams = mc.GetMethodParameters("Create");
        inParams["Name"] = Constants.ServiceName;
        inParams["DisplayName"] = Constants.ServiceDisplayName;
        inParams["PathName"] = BuildServiceCommand(plan);
        inParams["ServiceType"] = 16;
        inParams["ErrorControl"] = 1;
        inParams["StartMode"] = "Automatic";
        inParams["DesktopInteract"] = false;
        inParams["StartName"] = "LocalSystem";

        var result = mc.InvokeMethod("Create", inParams, null);
        var returnVal = Convert.ToInt32(result["ReturnValue"]);

        if (returnVal == 23)
        {
            StopService(computer);
            DeleteService(computer);

            for (var i = 0; i < 15; i++)
            {
                Thread.Sleep(1000);
                using var check = GetServiceObject(computer.Host);
                if (check == null) break;
            }

            result = mc.InvokeMethod("Create", inParams, null);
            returnVal = Convert.ToInt32(result["ReturnValue"]);
        }

        if (returnVal != 0)
            throw new InvalidOperationException(returnVal == 16
                ? "Служба помечена для удаления и ещё не освобождена SCM. Закройте оснастку «Службы» и повторите попытку."
                : $"Win32_Service.Create вернул код {returnVal}");
    }

    private void StartService(ComputerConfig computer)
    {
        using var service = GetServiceObject(computer.Host);
        if (service == null)
            throw new InvalidOperationException($"Служба '{Constants.ServiceName}' не найдена после установки.");

        var startedAt = DateTime.UtcNow;
        var startResult = Convert.ToInt32(service.InvokeMethod("StartService", null) ?? 0);
        // 0 = success, 7 = request timeout (service may still start), 10 = already running
        if (startResult != 0 && startResult != 7 && startResult != 10)
            throw new InvalidOperationException($"Win32_Service.StartService вернул код {startResult}.");

        for (var i = 0; i < 30; i++)
        {
            service.Get();
            var state = Convert.ToString(service["State"]);
            if (string.Equals(state, "Running", StringComparison.OrdinalIgnoreCase))
                return;

            Thread.Sleep(1000);
        }

        service.Get();
        var finalState = Convert.ToString(service["State"]) ?? "Unknown";
        var exitCode = Convert.ToString(service["ExitCode"]) ?? "Unknown";
        var logSnippet = TryGetRecentEventLogErrors(computer.Host, startedAt);
        var detail = logSnippet != null ? $" Журнал событий: {logSnippet}" : string.Empty;
        throw new InvalidOperationException(
            $"Служба не перешла в состояние Running. Текущее состояние: {finalState}, ExitCode: {exitCode}.{detail}");
    }

    private string? TryGetRecentEventLogErrors(string host, DateTime since)
    {
        try
        {
            var scope = WmiScope(host);
            var wmiTime = since.ToUniversalTime().ToString("yyyyMMddHHmmss.000000+000");
            var query = new ObjectQuery(
                "SELECT Message, SourceName FROM Win32_NTLogEvent " +
                $"WHERE Logfile='Application' AND TimeGenerated > '{wmiTime}' " +
                $"AND (SourceName='{Constants.ServiceName}' OR SourceName='.NET Runtime' OR SourceName='Application Error') " +
                "AND EventType <= 2");
            using var searcher = new ManagementObjectSearcher(scope, query);
            var messages = searcher.Get()
                .Cast<ManagementObject>()
                .Select(e => Convert.ToString(e["Message"])?.Trim())
                .Where(m => !string.IsNullOrEmpty(m))
                .Take(3)
                .ToList();
            return messages.Count > 0 ? string.Join(" | ", messages) : null;
        }
        catch
        {
            return null;
        }
    }

    private void StopService(ComputerConfig computer)
    {
        using var service = GetServiceObject(computer.Host);
        if (service == null) return;

        service.InvokeMethod("StopService", null);

        for (var i = 0; i < 30; i++)
        {
            service.Get();
            var state = Convert.ToString(service["State"]);
            if (string.Equals(state, "Stopped", StringComparison.OrdinalIgnoreCase))
                return;
            Thread.Sleep(1000);
        }
    }

    private void DeleteService(ComputerConfig computer)
    {
        using var service = GetServiceObject(computer.Host);
        service?.InvokeMethod("Delete", null);
    }

    private ManagementObject? GetServiceObject(string host)
    {
        var scope = WmiScope(host);
        var query = new ObjectQuery($"SELECT * FROM Win32_Service WHERE Name='{Constants.ServiceName}'");
        using var searcher = new ManagementObjectSearcher(scope, query);
        return searcher.Get().Cast<ManagementObject>().FirstOrDefault();
    }

    private ManagementScope WmiScope(string host)
    {
        var path = $@"\\{host}\root\cimv2";
        var scope = _username != null && !IsLoopback(host)
            ? new ManagementScope(path, new ConnectionOptions
              {
                  Username = _username,
                  Password = _password,
                  Impersonation = ImpersonationLevel.Impersonate,
                  Authentication = AuthenticationLevel.PacketPrivacy
              })
            : new ManagementScope(path);
        scope.Connect();
        return scope;
    }

    private ManagementScope WmiScopeDefault(string host)
    {
        var path = $@"\\{host}\root\default";
        var scope = _username != null && !IsLoopback(host)
            ? new ManagementScope(path, new ConnectionOptions
              {
                  Username = _username,
                  Password = _password,
                  Impersonation = ImpersonationLevel.Impersonate,
                  Authentication = AuthenticationLevel.PacketPrivacy
              })
            : new ManagementScope(path);
        scope.Connect();
        return scope;
    }

    private static bool IsLoopback(string host)
    {
        var h = host.Trim().ToLowerInvariant();
        return h is "127.0.0.1" or "localhost" or "::1";
    }

    private static void ConnectSmb(string unc, string? username, string? password)
    {
        if (username == null) return;
        var nr = new NETRESOURCE { dwType = 1, lpRemoteName = unc };
        var result = WNetAddConnection2(ref nr, password, username, 0);
        if (result != 0)
        {
            var hint = result switch
            {
                53 => "Сетевой путь не найден. Убедитесь, что компьютер доступен и общий ресурс Admin$ открыт.",
                67 => "Имя сети не найдено. Убедитесь, что хост доступен и общий ресурс Admin$ включён.",
                86 => "Неверный пароль.",
                1219 => "Уже существует подключение к этому серверу с другими учётными данными. Закройте все сетевые подключения к хосту и повторите попытку.",
                1326 => "Неверные учётные данные. Проверьте имя пользователя и пароль.",
                _ => "Проверьте учётные данные и доступность хоста."
            };
            throw new InvalidOperationException($"WNetAddConnection2 вернул код {result}. {hint}");
        }
    }

    private static void DisconnectSmb(string unc)
    {
        WNetCancelConnection2(unc, 0, true);
    }
}
