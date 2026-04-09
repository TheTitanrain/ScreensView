using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using ScreensView.Shared;
using ScreensView.Shared.Models;
using ScreensView.Viewer.Views;

namespace ScreensView.Tests;

public sealed class AddEditComputerWindowTests
{
    [Fact]
    public void CreateMode_SetsDefaultTitlePrimaryActionAndDefaults()
    {
        var snapshot = RunOnSta(() =>
        {
            var window = new AddEditComputerWindow(null);
            return new
            {
                window.Title,
                PrimaryButtonText = GetButton(window, "PrimaryActionButton").Content?.ToString(),
                PortText = GetTextBox(window, "PortBox").Text,
                ApiKeyText = GetTextBox(window, "ApiKeyBox").Text,
                GenerateEnabled = GetButton(window, "GenerateApiKeyButton").IsEnabled
            };
        });

        Assert.Equal("Добавить компьютер", snapshot.Title);
        Assert.Equal("Добавить", snapshot.PrimaryButtonText);
        Assert.Equal(Constants.DefaultPort.ToString(), snapshot.PortText);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.ApiKeyText));
        Assert.True(snapshot.GenerateEnabled);
    }

    [Fact]
    public void EditMode_PopulatesFieldsAndDisablesKeyGeneration()
    {
        var existing = new ComputerConfig
        {
            Name = "Workstation",
            Host = "10.0.0.15",
            Port = 5544,
            ApiKey = "existing-key",
            IsEnabled = false,
            Description = "Main office",
            CertThumbprint = "CERT-1"
        };

        var snapshot = RunOnSta(() =>
        {
            var window = new AddEditComputerWindow(existing);
            return new
            {
                window.Title,
                PrimaryButtonText = GetButton(window, "PrimaryActionButton").Content?.ToString(),
                NameText = GetTextBox(window, "NameBox").Text,
                HostText = GetTextBox(window, "HostBox").Text,
                PortText = GetTextBox(window, "PortBox").Text,
                ApiKeyText = GetTextBox(window, "ApiKeyBox").Text,
                DescriptionText = GetTextBox(window, "DescriptionBox").Text,
                Enabled = GetCheckBox(window, "EnabledCheck").IsChecked,
                GenerateEnabled = GetButton(window, "GenerateApiKeyButton").IsEnabled
            };
        });

        Assert.Equal("Редактировать компьютер", snapshot.Title);
        Assert.Equal("Сохранить", snapshot.PrimaryButtonText);
        Assert.Equal(existing.Name, snapshot.NameText);
        Assert.Equal(existing.Host, snapshot.HostText);
        Assert.Equal(existing.Port.ToString(), snapshot.PortText);
        Assert.Equal(existing.ApiKey, snapshot.ApiKeyText);
        Assert.Equal(existing.Description, snapshot.DescriptionText);
        Assert.False(snapshot.Enabled);
        Assert.False(snapshot.GenerateEnabled);
    }

    [Fact]
    public void BuildResult_WhenOnlyNameAndDescriptionChange_PreservesCertThumbprintAndTrimsFields()
    {
        var existing = new ComputerConfig
        {
            Name = "Old",
            Host = "10.0.0.20",
            Port = 5443,
            ApiKey = "old-key",
            IsEnabled = true,
            Description = "Old description",
            CertThumbprint = "CERT-KEEP"
        };

        var result = RunOnSta(() =>
        {
            var window = new AddEditComputerWindow(existing);
            GetTextBox(window, "NameBox").Text = "  New name  ";
            GetTextBox(window, "HostBox").Text = $"  {existing.Host}  ";
            GetTextBox(window, "PortBox").Text = existing.Port.ToString();
            GetTextBox(window, "ApiKeyBox").Text = "  new-key  ";
            GetTextBox(window, "DescriptionBox").Text = "  updated description  ";

            var built = InvokeBuildResult(window);
            return Assert.IsType<ComputerConfig>(built);
        });

        Assert.Equal("New name", result.Name);
        Assert.Equal(existing.Host, result.Host);
        Assert.Equal(existing.Port, result.Port);
        Assert.Equal("new-key", result.ApiKey);
        Assert.Equal("updated description", result.Description);
        Assert.Equal("CERT-KEEP", result.CertThumbprint);
    }

    [Fact]
    public void BuildResult_WhenHostChanges_ClearsCertThumbprint()
    {
        var existing = new ComputerConfig
        {
            Name = "Old",
            Host = "10.0.0.20",
            Port = 5443,
            ApiKey = "old-key",
            IsEnabled = true,
            CertThumbprint = "CERT-RESET"
        };

        var result = RunOnSta(() =>
        {
            var window = new AddEditComputerWindow(existing);
            GetTextBox(window, "NameBox").Text = existing.Name;
            GetTextBox(window, "HostBox").Text = "10.0.0.21";
            GetTextBox(window, "PortBox").Text = existing.Port.ToString();
            GetTextBox(window, "ApiKeyBox").Text = existing.ApiKey;
            return Assert.IsType<ComputerConfig>(InvokeBuildResult(window));
        });

        Assert.Equal(string.Empty, result.CertThumbprint);
    }

    [Fact]
    public void BuildResult_WhenPortChanges_ClearsCertThumbprint()
    {
        var existing = new ComputerConfig
        {
            Name = "Old",
            Host = "10.0.0.20",
            Port = 5443,
            ApiKey = "old-key",
            IsEnabled = true,
            CertThumbprint = "CERT-RESET"
        };

        var result = RunOnSta(() =>
        {
            var window = new AddEditComputerWindow(existing);
            GetTextBox(window, "NameBox").Text = existing.Name;
            GetTextBox(window, "HostBox").Text = existing.Host;
            GetTextBox(window, "PortBox").Text = "5444";
            GetTextBox(window, "ApiKeyBox").Text = existing.ApiKey;
            return Assert.IsType<ComputerConfig>(InvokeBuildResult(window));
        });

        Assert.Equal(string.Empty, result.CertThumbprint);
    }

    [Fact]
    public void BuildResult_WhenFieldsAreInvalid_ReturnsValidationError()
    {
        var error = RunOnSta(() =>
        {
            var window = new AddEditComputerWindow(null);
            GetTextBox(window, "NameBox").Text = "";
            GetTextBox(window, "HostBox").Text = "";
            GetTextBox(window, "PortBox").Text = "0";
            GetTextBox(window, "ApiKeyBox").Text = "";
            return InvokeBuildResult(window);
        });

        Assert.IsType<string>(error);
    }

    private static object InvokeBuildResult(AddEditComputerWindow window)
    {
        var method = typeof(AddEditComputerWindow).GetMethod(
            "BuildResultOrValidationMessage",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(method);

        try
        {
            return method!.Invoke(window, [])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static TextBox GetTextBox(FrameworkElement window, string name)
        => Assert.IsType<TextBox>(window.FindName(name));

    private static Button GetButton(FrameworkElement window, string name)
        => Assert.IsType<Button>(window.FindName(name));

    private static CheckBox GetCheckBox(FrameworkElement window, string name)
        => Assert.IsType<CheckBox>(window.FindName(name));

    private static T RunOnSta<T>(Func<T> func)
    {
        T? result = default;
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try { result = func(); }
            catch (Exception ex) { caught = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (caught is not null)
            ExceptionDispatchInfo.Capture(caught).Throw();

        return result!;
    }
}
