using ScreensView.Shared.Models;
using ScreensView.Viewer.Services;
using ScreensView.Viewer.ViewModels;

namespace ScreensView.Tests;

public class MainViewModelTests : IDisposable
{
    private readonly string _tempFile = Path.GetTempFileName();

    public void Dispose() => File.Delete(_tempFile);

    private MainViewModel CreateVm()
    {
        var storage = new ComputerStorageService(_tempFile);
        var poller = new ScreenshotPollerService(new AgentHttpClient());
        return new MainViewModel(storage, poller);
    }

    [Fact]
    public void AddComputers_AddsAllToCollection()
    {
        var vm = CreateVm();
        var configs = new[]
        {
            new ComputerConfig { Name = "PC-1", Host = "10.0.0.1", Port = 5443, ApiKey = "k1" },
            new ComputerConfig { Name = "PC-2", Host = "10.0.0.2", Port = 5443, ApiKey = "k2" },
            new ComputerConfig { Name = "PC-3", Host = "10.0.0.3", Port = 5443, ApiKey = "k3" },
        };

        vm.AddComputers(configs);

        Assert.Equal(3, vm.Computers.Count);
    }

    [Fact]
    public void AddComputers_PersistsAfterReload()
    {
        var storage = new ComputerStorageService(_tempFile);
        var configs = new[]
        {
            new ComputerConfig { Name = "PC-1", Host = "10.0.0.1", Port = 5443, ApiKey = "k1" },
            new ComputerConfig { Name = "PC-2", Host = "10.0.0.2", Port = 5443, ApiKey = "k2" },
        };

        using (var vm = new MainViewModel(storage, new ScreenshotPollerService(new AgentHttpClient())))
            vm.AddComputers(configs);

        using var vm2 = new MainViewModel(storage, new ScreenshotPollerService(new AgentHttpClient()));
        Assert.Equal(2, vm2.Computers.Count);
    }

    [Fact]
    public void AddComputers_AppendsToExistingCollection()
    {
        var vm = CreateVm();
        vm.AddComputer(new ComputerConfig { Name = "Existing", Host = "10.0.0.100", Port = 5443, ApiKey = "e1" });
        var newConfigs = new[]
        {
            new ComputerConfig { Name = "PC-1", Host = "10.0.0.1", Port = 5443, ApiKey = "k1" },
            new ComputerConfig { Name = "PC-2", Host = "10.0.0.2", Port = 5443, ApiKey = "k2" },
        };

        vm.AddComputers(newConfigs);

        Assert.Equal(3, vm.Computers.Count);
    }

    [Fact]
    public void AddComputers_EmptyList_DoesNotThrow()
    {
        var vm = CreateVm();
        var ex = Record.Exception(() => vm.AddComputers([]));
        Assert.Null(ex);
    }
}
