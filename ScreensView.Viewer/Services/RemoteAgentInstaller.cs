using System.IO;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using ScreensView.Shared;
using ScreensView.Shared.Models;

namespace ScreensView.Viewer.Services;

public class RemoteAgentInstaller
{
    private const string PayloadsRootFolderName = "AgentPayloads";
    private const uint HKeyLocalMachine = 0x80000002;
    private readonly Action<string, string, string> _log;

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

    public RemoteAgentInstaller(Action<string, string, string> log)
    {
        _log = log;
    }

    public async Task InstallAsync(ComputerConfig computer, string? username = null, string? password = null)
    {
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
                _log(computer.Name, "Копирование файлов", string.Empty);
                CopyAgentFiles(targetDir, computer, plan);
                _log(computer.Name, "Создание службы", string.Empty);
                CreateService(computer, plan);
                _log(computer.Name, "Запуск службы", string.Empty);
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
                _log(computer.Name, "Остановка службы", string.Empty);
                StopService(computer);
                _log(computer.Name, "Обновление файлов", string.Empty);
                CopyAgentFiles(targetDir, computer, plan);
                _log(computer.Name, "Запуск службы", string.Empty);
                StartService(computer);
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
                _log(computer.Name, "Остановка службы", string.Empty);
                StopService(computer);
                _log(computer.Name, "Удаление службы", string.Empty);
                DeleteService(computer);
                if (Directory.Exists(targetDir))
                {
                    _log(computer.Name, "Удаление файлов", string.Empty);
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

        foreach (var file in Directory.GetFiles(sourceDir, "*.*"))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith("ScreensView.Viewer", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(file, Path.Combine(targetDir, name), overwrite: true);
        }
    }

    internal static bool HasNetFramework48OrNewer(int? releaseKey) => releaseKey >= 528040;

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

    private static AgentDeploymentPlan ResolveDeploymentPlanForHost(string host)
    {
        var os = GetRemoteOperatingSystemInfo(host);
        var releaseKey = (os.IsWindows7 || os.IsLegacyServer) ? TryGetNetFrameworkReleaseKey(host) : null;
        return ResolveDeploymentPlan(os, releaseKey);
    }

    private static RemoteOperatingSystemInfo GetRemoteOperatingSystemInfo(string host)
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

    private static int? TryGetNetFrameworkReleaseKey(string host)
    {
        var scope = new ManagementScope($@"\\{host}\root\default");
        scope.Connect();

        using var reg = new ManagementClass(scope, new ManagementPath("StdRegProv"), null);
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

    private static void CreateService(ComputerConfig computer, AgentDeploymentPlan plan)
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

    private static void StartService(ComputerConfig computer)
    {
        using var service = GetServiceObject(computer.Host);
        if (service == null)
            throw new InvalidOperationException($"Служба '{Constants.ServiceName}' не найдена после установки.");

        var startResult = Convert.ToInt32(service.InvokeMethod("StartService", null) ?? 0);
        if (startResult != 0 && startResult != 10)
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
        throw new InvalidOperationException($"Служба не перешла в состояние Running. Текущее состояние: {finalState}, ExitCode: {exitCode}.");
    }

    private static void StopService(ComputerConfig computer)
    {
        using var service = GetServiceObject(computer.Host);
        service?.InvokeMethod("StopService", null);
    }

    private static void DeleteService(ComputerConfig computer)
    {
        using var service = GetServiceObject(computer.Host);
        service?.InvokeMethod("Delete", null);
    }

    private static ManagementObject? GetServiceObject(string host)
    {
        var scope = WmiScope(host);
        var query = new ObjectQuery($"SELECT * FROM Win32_Service WHERE Name='{Constants.ServiceName}'");
        using var searcher = new ManagementObjectSearcher(scope, query);
        return searcher.Get().Cast<ManagementObject>().FirstOrDefault();
    }

    private static ManagementScope WmiScope(string host)
    {
        var scope = new ManagementScope($@"\\{host}\root\cimv2");
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


