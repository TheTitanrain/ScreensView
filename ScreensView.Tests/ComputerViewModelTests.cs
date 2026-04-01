using System.Runtime.ExceptionServices;
using ScreensView.Shared.Models;
using ScreensView.Viewer.ViewModels;

namespace ScreensView.Tests;

public class ComputerViewModelTests
{
    private static ComputerConfig MakeConfig(Action<ComputerConfig>? configure = null)
    {
        var config = new ComputerConfig
        {
            Id = Guid.NewGuid(),
            Name = "Test PC",
            Host = "192.168.1.100",
            Port = 5443,
            ApiKey = "secret-key",
            IsEnabled = true,
            CertThumbprint = "AABBCC"
        };
        configure?.Invoke(config);
        return config;
    }

    [Fact]
    public void Constructor_MapsAllPropertiesFromConfig()
    {
        var config = MakeConfig();

        var vm = new ComputerViewModel(config);

        Assert.Equal(config.Id, vm.Id);
        Assert.Equal(config.Name, vm.Name);
        Assert.Equal(config.Host, vm.Host);
        Assert.Equal(config.Port, vm.Port);
        Assert.Equal(config.ApiKey, vm.ApiKey);
        Assert.Equal(config.IsEnabled, vm.IsEnabled);
        Assert.Equal(config.CertThumbprint, vm.CertThumbprint);
    }

    [Fact]
    public void Constructor_InitialStatus_IsUnknown()
    {
        var vm = new ComputerViewModel(MakeConfig());

        Assert.Equal(ComputerStatus.Unknown, vm.Status);
    }

    [Fact]
    public void Constructor_WhenComputerDisabled_SetsDisabledStatusAndMessage()
    {
        var vm = new ComputerViewModel(MakeConfig(config => config.IsEnabled = false));

        Assert.Equal(ComputerStatus.Disabled, vm.Status);
        Assert.Equal("Компьютер отключён в Управлении компьютерами.", vm.StatusMessage);
    }

    [Fact]
    public void ToConfig_RoundTripsAllProperties()
    {
        var original = MakeConfig();
        var vm = new ComputerViewModel(original);

        var result = vm.ToConfig();

        Assert.Equal(original.Id, result.Id);
        Assert.Equal(original.Name, result.Name);
        Assert.Equal(original.Host, result.Host);
        Assert.Equal(original.Port, result.Port);
        Assert.Equal(original.ApiKey, result.ApiKey);
        Assert.Equal(original.IsEnabled, result.IsEnabled);
        Assert.Equal(original.CertThumbprint, result.CertThumbprint);
    }

    [Fact]
    public void SetError_SetsOfflineStatusAndMessage()
    {
        var vm = new ComputerViewModel(MakeConfig());

        vm.SetError("Connection refused");

        Assert.Equal(ComputerStatus.Offline, vm.Status);
        Assert.Equal("Connection refused", vm.StatusMessage);
    }

    [Fact]
    public void SetError_CanBeCalledMultipleTimes()
    {
        var vm = new ComputerViewModel(MakeConfig());

        vm.SetError("First error");
        vm.SetError("Second error");

        Assert.Equal(ComputerStatus.Offline, vm.Status);
        Assert.Equal("Second error", vm.StatusMessage);
    }

    [Fact]
    public void IsEnabled_WhenSetToFalse_SetsDisabledStatusAndMessage()
    {
        var vm = new ComputerViewModel(MakeConfig());

        vm.IsEnabled = false;

        Assert.Equal(ComputerStatus.Disabled, vm.Status);
        Assert.Equal("Компьютер отключён в Управлении компьютерами.", vm.StatusMessage);
    }

    [Fact]
    public void IsEnabled_WhenSetBackToTrue_ResetsStatusToUnknownAndClearsMessage()
    {
        var vm = new ComputerViewModel(MakeConfig(config => config.IsEnabled = false));

        vm.IsEnabled = true;

        Assert.Equal(ComputerStatus.Unknown, vm.Status);
        Assert.Equal(string.Empty, vm.StatusMessage);
    }

    [Fact]
    public void UpdateScreenshot_SetsOnlineStatus()
    {
        var vm = new ComputerViewModel(MakeConfig());
        var response = new ScreenshotResponse
        {
            ImageBase64 = CreateMinimalJpegBase64(),
            Timestamp = DateTime.UtcNow,
            MachineName = "TEST-PC"
        };

        RunOnSta(() => vm.UpdateScreenshot(response));

        Assert.Equal(ComputerStatus.Online, vm.Status);
        Assert.NotNull(vm.Screenshot);
        Assert.Equal(response.Timestamp, vm.LastUpdated);
        Assert.Equal(string.Empty, vm.StatusMessage);
    }

    [Fact]
    public void UpdateScreenshot_AfterSetError_ResetsToOnline()
    {
        var vm = new ComputerViewModel(MakeConfig());
        vm.SetError("Was offline");

        var response = new ScreenshotResponse
        {
            ImageBase64 = CreateMinimalJpegBase64(),
            Timestamp = DateTime.UtcNow,
            MachineName = "TEST-PC"
        };

        RunOnSta(() => vm.UpdateScreenshot(response));

        Assert.Equal(ComputerStatus.Online, vm.Status);
        Assert.Equal(string.Empty, vm.StatusMessage);
    }

    [Fact]
    public void UpdateScreenshot_WhenComputerDisabled_DoesNotOverrideDisabledStatus()
    {
        var vm = new ComputerViewModel(MakeConfig(config => config.IsEnabled = false));
        var response = new ScreenshotResponse
        {
            ImageBase64 = CreateMinimalJpegBase64(),
            Timestamp = DateTime.UtcNow,
            MachineName = "TEST-PC"
        };

        RunOnSta(() => vm.UpdateScreenshot(response));

        Assert.Equal(ComputerStatus.Disabled, vm.Status);
        Assert.Equal("Компьютер отключён в Управлении компьютерами.", vm.StatusMessage);
        Assert.Null(vm.Screenshot);
        Assert.Null(vm.LastUpdated);
    }

    private static string CreateMinimalJpegBase64()
    {
        using var bmp = new System.Drawing.Bitmap(4, 4);
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
        return Convert.ToBase64String(ms.ToArray());
    }

    private static void RunOnSta(Action action)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { caught = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (caught != null) ExceptionDispatchInfo.Capture(caught).Throw();
    }
}
