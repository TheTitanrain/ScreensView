# Bulk Add Computers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Добавить несколько" button to `ComputersManagerWindow` that opens a tabbed dialog for bulk-adding computers by host list or IP range.

**Architecture:** New static class `BulkComputerParser` handles all parsing and deduplication. New `AddMultipleComputersWindow` provides the UI with two tabs. `MainViewModel` gets a single `AddComputers` method that adds all configs and saves once.

**Tech Stack:** WPF (.NET 8), xUnit, C# 12, `System.Net.NetworkInformation.IPAddress` for IP parsing.

---

## File Map

| File | Action | Responsibility |
|---|---|---|
| `ScreensView.Viewer/Services/BulkComputerParser.cs` | **Create** | ParseHosts, ParseIpRange, GenerateApiKey |
| `ScreensView.Tests/BulkComputerParserTests.cs` | **Create** | Unit tests for all parsing / dedup logic |
| `ScreensView.Tests/MainViewModelTests.cs` | **Create** | Unit tests for AddComputers |
| `ScreensView.Viewer/ViewModels/MainViewModel.cs` | **Modify** | Add `AddComputers(IEnumerable<ComputerConfig>)` |
| `ScreensView.Viewer/Views/AddEditComputerWindow.xaml.cs` | **Modify** | Replace private `GenerateApiKey()` with `BulkComputerParser.GenerateApiKey()` |
| `ScreensView.Viewer/Views/AddMultipleComputersWindow.xaml` | **Create** | Dialog XAML with TabControl |
| `ScreensView.Viewer/Views/AddMultipleComputersWindow.xaml.cs` | **Create** | Code-behind: calls BulkComputerParser, builds Results |
| `ScreensView.Viewer/Views/ComputersManagerWindow.xaml` | **Modify** | Add `BtnAddMultiple` to toolbar |
| `ScreensView.Viewer/Views/ComputersManagerWindow.xaml.cs` | **Modify** | Add `AddMultiple_Click` handler |

---

## Task 1: Create BulkComputerParser with tests (TDD)

**Files:**
- Create: `ScreensView.Viewer/Services/BulkComputerParser.cs`
- Create: `ScreensView.Tests/BulkComputerParserTests.cs`

---

- [ ] **Step 1.1: Create the test file skeleton**

Create `ScreensView.Tests/BulkComputerParserTests.cs`:

```csharp
using System.Net.Sockets;
using ScreensView.Shared;
using ScreensView.Viewer.Services;

namespace ScreensView.Tests;

public class BulkComputerParserTests
{
    private static readonly ISet<string> NoExisting =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
```

- [ ] **Step 1.2: Write failing tests for ParseHosts**

Append to `BulkComputerParserTests`:

```csharp
[Fact]
public void ParseHosts_SingleLine_ReturnsOneConfig()
{
    var result = BulkComputerParser.ParseHosts("192.168.1.1", 5443, NoExisting);

    Assert.Single(result);
    Assert.Equal("192.168.1.1", result[0].Host);
    Assert.Equal("192.168.1.1", result[0].Name);
    Assert.Equal(5443, result[0].Port);
    Assert.True(result[0].IsEnabled);
    Assert.NotEmpty(result[0].ApiKey);
}

[Fact]
public void ParseHosts_MultipleLines_ReturnsOnePerNonEmptyLine()
{
    var input = "host1\nhost2\nhost3";
    var result = BulkComputerParser.ParseHosts(input, 5443, NoExisting);
    Assert.Equal(3, result.Count);
}

[Fact]
public void ParseHosts_BlankLinesIgnored()
{
    var input = "host1\n\n  \nhost2";
    var result = BulkComputerParser.ParseHosts(input, 5443, NoExisting);
    Assert.Equal(2, result.Count);
}

[Fact]
public void ParseHosts_CrLfLineEndings_Handled()
{
    var input = "host1\r\nhost2\r\nhost3";
    var result = BulkComputerParser.ParseHosts(input, 5443, NoExisting);
    Assert.Equal(3, result.Count);
}

[Fact]
public void ParseHosts_IntraListDuplicates_FirstOccurrenceWins()
{
    var input = "192.168.1.1\n192.168.1.1\n192.168.1.2";
    var result = BulkComputerParser.ParseHosts(input, 5443, NoExisting);
    Assert.Equal(2, result.Count);
    Assert.All(result, c => Assert.NotEqual(result[0].Host, result[1].Host));
}

[Fact]
public void ParseHosts_DuplicateCaseInsensitive_Deduplicated()
{
    var input = "MyHost\nmyhost";
    var result = BulkComputerParser.ParseHosts(input, 5443, NoExisting);
    Assert.Single(result);
}

[Fact]
public void ParseHosts_ExistingHostSkipped()
{
    var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "192.168.1.1" };
    var result = BulkComputerParser.ParseHosts("192.168.1.1\n192.168.1.2", 5443, existing);
    Assert.Single(result);
    Assert.Equal("192.168.1.2", result[0].Host);
}

[Fact]
public void ParseHosts_UniqueApiKeyPerEntry()
{
    var result = BulkComputerParser.ParseHosts("host1\nhost2", 5443, NoExisting);
    Assert.NotEqual(result[0].ApiKey, result[1].ApiKey);
}

[Fact]
public void ParseHosts_EmptyInput_ReturnsEmpty()
{
    var result = BulkComputerParser.ParseHosts("", 5443, NoExisting);
    Assert.Empty(result);
}
```

- [ ] **Step 1.3: Write failing tests for ParseIpRange**

Append to `BulkComputerParserTests`:

```csharp
[Fact]
public void ParseIpRange_ValidRange_ReturnsCorrectCount()
{
    var result = BulkComputerParser.ParseIpRange(
        "192.168.1.10", "192.168.1.12", 5443, NoExisting, out var error);

    Assert.Null(error);
    Assert.Equal(3, result.Count);
    Assert.Equal("192.168.1.10", result[0].Host);
    Assert.Equal("192.168.1.11", result[1].Host);
    Assert.Equal("192.168.1.12", result[2].Host);
}

[Fact]
public void ParseIpRange_SingleAddress_ReturnsOne()
{
    var result = BulkComputerParser.ParseIpRange(
        "10.0.0.5", "10.0.0.5", 5443, NoExisting, out var error);

    Assert.Null(error);
    Assert.Single(result);
}

[Fact]
public void ParseIpRange_InvalidStartIp_ReturnsError()
{
    BulkComputerParser.ParseIpRange("not-an-ip", "10.0.0.5", 5443, NoExisting, out var error);
    Assert.NotNull(error);
}

[Fact]
public void ParseIpRange_InvalidEndIp_ReturnsError()
{
    BulkComputerParser.ParseIpRange("10.0.0.1", "999.999.999.999", 5443, NoExisting, out var error);
    Assert.NotNull(error);
}

[Fact]
public void ParseIpRange_IPv6_ReturnsError()
{
    BulkComputerParser.ParseIpRange(
        "::1", "::2", 5443, NoExisting, out var error);
    Assert.NotNull(error);
    Assert.Contains("IPv4", error, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public void ParseIpRange_EndLessThanStart_ReturnsError()
{
    BulkComputerParser.ParseIpRange(
        "192.168.1.20", "192.168.1.10", 5443, NoExisting, out var error);
    Assert.NotNull(error);
}

[Fact]
public void ParseIpRange_Exactly255Addresses_Allowed()
{
    // 192.168.1.0 to 192.168.1.254 = 255 addresses
    var result = BulkComputerParser.ParseIpRange(
        "192.168.1.0", "192.168.1.254", 5443, NoExisting, out var error);
    Assert.Null(error);
    Assert.Equal(255, result.Count);
}

[Fact]
public void ParseIpRange_256Addresses_ReturnsError()
{
    // 192.168.1.0 to 192.168.1.255 = 256 addresses
    BulkComputerParser.ParseIpRange(
        "192.168.1.0", "192.168.1.255", 5443, NoExisting, out var error);
    Assert.NotNull(error);
}

[Fact]
public void ParseIpRange_ExistingHostsSkipped()
{
    var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "192.168.1.11" };
    var result = BulkComputerParser.ParseIpRange(
        "192.168.1.10", "192.168.1.12", 5443, existing, out var error);
    Assert.Null(error);
    Assert.Equal(2, result.Count);
    Assert.DoesNotContain(result, c => c.Host == "192.168.1.11");
}
```

- [ ] **Step 1.4: Write failing test for GenerateApiKey**

Append to `BulkComputerParserTests`:

```csharp
[Fact]
public void GenerateApiKey_IsHexString_Length64()
{
    var key = BulkComputerParser.GenerateApiKey();
    Assert.Equal(64, key.Length);
    Assert.Matches("^[0-9a-f]+$", key);
}

[Fact]
public void GenerateApiKey_EachCallUnique()
{
    var key1 = BulkComputerParser.GenerateApiKey();
    var key2 = BulkComputerParser.GenerateApiKey();
    Assert.NotEqual(key1, key2);
}
```

- [ ] **Step 1.5: Run tests to confirm they fail**

```bash
dotnet test ScreensView.Tests --filter "FullyQualifiedName~BulkComputerParserTests" --no-build 2>&1 | tail -5
```

Expected: build error or test failure (class doesn't exist yet).

- [ ] **Step 1.6: Create BulkComputerParser**

Create `ScreensView.Viewer/Services/BulkComputerParser.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using ScreensView.Shared;
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
            var host = line.Trim().TrimEnd('\r');
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
```

- [ ] **Step 1.7: Build the solution**

```bash
dotnet build ScreensView.Viewer/ScreensView.Viewer.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 1.8: Run BulkComputerParser tests**

```bash
dotnet test ScreensView.Tests --filter "FullyQualifiedName~BulkComputerParserTests"
```

Expected: All tests pass.

- [ ] **Step 1.9: Commit**

```bash
git add ScreensView.Viewer/Services/BulkComputerParser.cs ScreensView.Tests/BulkComputerParserTests.cs
git commit -m "feat: add BulkComputerParser with host/IP-range parsing and deduplication"
```

---

## Task 2: Update AddEditComputerWindow to use BulkComputerParser.GenerateApiKey

**Files:**
- Modify: `ScreensView.Viewer/Views/AddEditComputerWindow.xaml.cs`

---

- [ ] **Step 2.1: Replace private GenerateApiKey in AddEditComputerWindow**

In `ScreensView.Viewer/Views/AddEditComputerWindow.xaml.cs`:

Remove the `using System.Security.Cryptography;` import (no longer needed).

Replace:
```csharp
private static string GenerateApiKey()
{
    var bytes = RandomNumberGenerator.GetBytes(32);
    return Convert.ToHexString(bytes).ToLower();
}
```

With:
```csharp
private static string GenerateApiKey() => BulkComputerParser.GenerateApiKey();
```

Add `using ScreensView.Viewer.Services;` at the top.

- [ ] **Step 2.2: Build and verify**

```bash
dotnet build ScreensView.Viewer/ScreensView.Viewer.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 2.3: Run all tests**

```bash
dotnet test ScreensView.Tests
```

Expected: All tests pass.

- [ ] **Step 2.4: Commit**

```bash
git add ScreensView.Viewer/Views/AddEditComputerWindow.xaml.cs
git commit -m "refactor: delegate GenerateApiKey to BulkComputerParser"
```

---

## Task 3: Add AddComputers to MainViewModel with tests (TDD)

**Files:**
- Create: `ScreensView.Tests/MainViewModelTests.cs`
- Modify: `ScreensView.Viewer/ViewModels/MainViewModel.cs`

---

- [ ] **Step 3.1: Create MainViewModelTests**

Create `ScreensView.Tests/MainViewModelTests.cs`:

```csharp
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
        var vm = new MainViewModel(storage, new ScreenshotPollerService(new AgentHttpClient()));
        var configs = new[]
        {
            new ComputerConfig { Name = "PC-1", Host = "10.0.0.1", Port = 5443, ApiKey = "k1" },
            new ComputerConfig { Name = "PC-2", Host = "10.0.0.2", Port = 5443, ApiKey = "k2" },
        };

        vm.AddComputers(configs);

        // reload from the same file
        var vm2 = new MainViewModel(storage, new ScreenshotPollerService(new AgentHttpClient()));
        Assert.Equal(2, vm2.Computers.Count);
    }

    [Fact]
    public void AddComputers_EmptyList_DoesNotThrow()
    {
        var vm = CreateVm();
        var ex = Record.Exception(() => vm.AddComputers([]));
        Assert.Null(ex);
    }
}
```

- [ ] **Step 3.2: Run tests to confirm they fail**

```bash
dotnet test ScreensView.Tests --filter "FullyQualifiedName~MainViewModelTests"
```

Expected: Compile error — `AddComputers` does not exist yet.

- [ ] **Step 3.3: Add AddComputers to MainViewModel**

In `ScreensView.Viewer/ViewModels/MainViewModel.cs`, after the `AddComputer` method (line 56), add:

```csharp
public void AddComputers(IEnumerable<ComputerConfig> configs)
{
    foreach (var config in configs)
        Computers.Add(new ComputerViewModel(config));
    SaveComputers();
}
```

- [ ] **Step 3.4: Run MainViewModelTests**

```bash
dotnet test ScreensView.Tests --filter "FullyQualifiedName~MainViewModelTests"
```

Expected: All 3 tests pass.

- [ ] **Step 3.5: Run all tests**

```bash
dotnet test ScreensView.Tests
```

Expected: All tests pass.

- [ ] **Step 3.6: Commit**

```bash
git add ScreensView.Viewer/ViewModels/MainViewModel.cs ScreensView.Tests/MainViewModelTests.cs
git commit -m "feat: add MainViewModel.AddComputers with batch save"
```

---

## Task 4: Create AddMultipleComputersWindow

**Files:**
- Create: `ScreensView.Viewer/Views/AddMultipleComputersWindow.xaml`
- Create: `ScreensView.Viewer/Views/AddMultipleComputersWindow.xaml.cs`

---

- [ ] **Step 4.1: Create the XAML**

Create `ScreensView.Viewer/Views/AddMultipleComputersWindow.xaml`:

```xml
<Window x:Class="ScreensView.Viewer.Views.AddMultipleComputersWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Добавить несколько компьютеров" Height="380" Width="460"
        WindowStartupLocation="CenterOwner" ResizeMode="CanResizeWithGrip">
    <DockPanel Margin="12">
        <StackPanel DockPanel.Dock="Bottom" Orientation="Horizontal"
                    HorizontalAlignment="Right" Margin="0,10,0,0">
            <Button x:Name="BtnAdd" Content="Добавить" Width="100" Margin="0,0,8,0"
                    IsDefault="True" Click="Add_Click"/>
            <Button Content="Отмена" Width="80" IsCancel="True"/>
        </StackPanel>

        <TabControl x:Name="Tabs" SelectionChanged="Tabs_SelectionChanged">

            <!-- Tab 1: By hosts -->
            <TabItem Header="По хостам">
                <Grid Margin="8">
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="*"/>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="Auto"/>
                    </Grid.RowDefinitions>

                    <Label Grid.Row="0" Content="Хосты (по одному на строку):"/>
                    <TextBox Grid.Row="1" x:Name="HostsBox" AcceptsReturn="True"
                             VerticalScrollBarVisibility="Auto"
                             FontFamily="Consolas" Margin="0,0,0,8"
                             TextChanged="HostsBox_TextChanged"/>
                    <Grid Grid.Row="2" Margin="0,0,0,8">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="Auto"/>
                            <ColumnDefinition Width="80"/>
                        </Grid.ColumnDefinitions>
                        <Label Grid.Column="0" Content="Порт:" VerticalAlignment="Center"/>
                        <TextBox Grid.Column="1" x:Name="HostsPortBox"
                                 TextChanged="HostsBox_TextChanged"/>
                    </Grid>
                    <TextBlock Grid.Row="3" x:Name="HostsCountLabel"
                               Foreground="Gray" FontStyle="Italic"/>
                </Grid>
            </TabItem>

            <!-- Tab 2: By IP range -->
            <TabItem Header="По диапазону IP">
                <Grid Margin="8">
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="*"/>
                    </Grid.RowDefinitions>

                    <Grid Grid.Row="0" Margin="0,0,0,8">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="Auto"/>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="Auto"/>
                            <ColumnDefinition Width="*"/>
                        </Grid.ColumnDefinitions>
                        <Label Grid.Column="0" Content="С:" VerticalAlignment="Center"/>
                        <TextBox Grid.Column="1" x:Name="StartIpBox" Margin="0,0,8,0"
                                 TextChanged="IpRange_TextChanged"/>
                        <Label Grid.Column="2" Content="По:" VerticalAlignment="Center"/>
                        <TextBox Grid.Column="3" x:Name="EndIpBox"
                                 TextChanged="IpRange_TextChanged"/>
                    </Grid>

                    <Grid Grid.Row="1" Margin="0,0,0,8">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="Auto"/>
                            <ColumnDefinition Width="80"/>
                        </Grid.ColumnDefinitions>
                        <Label Grid.Column="0" Content="Порт:" VerticalAlignment="Center"/>
                        <TextBox Grid.Column="1" x:Name="RangePortBox"
                                 TextChanged="IpRange_TextChanged"/>
                    </Grid>

                    <TextBlock Grid.Row="2" x:Name="RangeStatusLabel"
                               Foreground="Gray" FontStyle="Italic" TextWrapping="Wrap"/>
                </Grid>
            </TabItem>

        </TabControl>
    </DockPanel>
</Window>
```

- [ ] **Step 4.2: Create the code-behind**

Create `ScreensView.Viewer/Views/AddMultipleComputersWindow.xaml.cs`:

```csharp
using System.Windows;
using ScreensView.Shared;
using ScreensView.Shared.Models;
using ScreensView.Viewer.Services;

namespace ScreensView.Viewer.Views;

public partial class AddMultipleComputersWindow : Window
{
    private readonly ISet<string> _existingHosts;

    public List<ComputerConfig> Results { get; private set; } = [];

    public AddMultipleComputersWindow(ISet<string> existingHosts)
    {
        InitializeComponent();
        _existingHosts = existingHosts;
        HostsPortBox.Text = Constants.DefaultPort.ToString();
        RangePortBox.Text = Constants.DefaultPort.ToString();
        UpdateHostsCounter();
        UpdateRangeStatus();
    }

    private bool IsRangeTab => Tabs.SelectedIndex == 1;

    // ── Hosts tab ───────────────────────────────────────────────────────────

    private void HostsBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => UpdateHostsCounter();

    private void UpdateHostsCounter()
    {
        var hosts = BulkComputerParser.ParseHosts(
            HostsBox.Text,
            int.TryParse(HostsPortBox.Text, out var p) ? p : 0,
            _existingHosts);
        HostsCountLabel.Text = hosts.Count > 0
            ? $"{hosts.Count} компьютер(а/ов) будет добавлено"
            : string.Empty;
        BtnAdd.IsEnabled = true;
        BtnAdd.Content = hosts.Count > 0 ? $"Добавить ({hosts.Count})" : "Добавить";
    }

    // ── IP range tab ────────────────────────────────────────────────────────

    private void IpRange_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => UpdateRangeStatus();

    private void UpdateRangeStatus()
    {
        if (string.IsNullOrWhiteSpace(StartIpBox.Text) &&
            string.IsNullOrWhiteSpace(EndIpBox.Text))
        {
            RangeStatusLabel.Text = string.Empty;
            RangeStatusLabel.Foreground = System.Windows.Media.Brushes.Gray;
            BtnAdd.IsEnabled = false;
            BtnAdd.Content = "Добавить";
            return;
        }

        BulkComputerParser.ParseIpRange(
            StartIpBox.Text, EndIpBox.Text,
            int.TryParse(RangePortBox.Text, out var p) ? p : 0,
            _existingHosts, out var error);

        if (error != null)
        {
            RangeStatusLabel.Text = error;
            RangeStatusLabel.Foreground = System.Windows.Media.Brushes.Red;
            BtnAdd.IsEnabled = false;
            BtnAdd.Content = "Добавить";
        }
        else
        {
            var result = BulkComputerParser.ParseIpRange(
                StartIpBox.Text, EndIpBox.Text, p, _existingHosts, out _);
            RangeStatusLabel.Text = $"{result.Count} компьютер(а/ов) будет добавлено";
            RangeStatusLabel.Foreground = System.Windows.Media.Brushes.Gray;
            BtnAdd.IsEnabled = true;
            BtnAdd.Content = $"Добавить ({result.Count})";
        }
    }

    private void Tabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (IsRangeTab)
            UpdateRangeStatus();
        else
            UpdateHostsCounter();
    }

    // ── OK ──────────────────────────────────────────────────────────────────

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (!IsRangeTab)
        {
            if (!int.TryParse(HostsPortBox.Text, out int port) || port < 1 || port > 65535)
            {
                MessageBox.Show("Введите корректный порт (1–65535).", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Results = [.. BulkComputerParser.ParseHosts(HostsBox.Text, port, _existingHosts)];
            if (Results.Count == 0)
            {
                MessageBox.Show("Введите хотя бы один хост.", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        else
        {
            if (!int.TryParse(RangePortBox.Text, out int port) || port < 1 || port > 65535)
            {
                MessageBox.Show("Введите корректный порт (1–65535).", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Results = [.. BulkComputerParser.ParseIpRange(
                StartIpBox.Text, EndIpBox.Text, port, _existingHosts, out _)];
        }

        DialogResult = true;
    }
}
```

- [ ] **Step 4.3: Build the solution**

```bash
dotnet build ScreensView.Viewer/ScreensView.Viewer.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4.4: Commit**

```bash
git add ScreensView.Viewer/Views/AddMultipleComputersWindow.xaml ScreensView.Viewer/Views/AddMultipleComputersWindow.xaml.cs
git commit -m "feat: add AddMultipleComputersWindow with hosts and IP-range tabs"
```

---

## Task 5: Wire up ComputersManagerWindow

**Files:**
- Modify: `ScreensView.Viewer/Views/ComputersManagerWindow.xaml`
- Modify: `ScreensView.Viewer/Views/ComputersManagerWindow.xaml.cs`

---

- [ ] **Step 5.1: Add BtnAddMultiple to toolbar XAML**

In `ScreensView.Viewer/Views/ComputersManagerWindow.xaml`, after the `BtnAdd` button (line 10):

```xml
<Button x:Name="BtnAddMultiple" Content="Добавить несколько" Padding="8,3" Margin="2,0" Click="AddMultiple_Click"/>
```

So the toolbar section becomes:

```xml
<Button x:Name="BtnAdd" Content="Добавить" Padding="8,3" Margin="2,0" Click="Add_Click"/>
<Button x:Name="BtnAddMultiple" Content="Добавить несколько" Padding="8,3" Margin="2,0" Click="AddMultiple_Click"/>
<Button x:Name="BtnEdit" Content="Редактировать" Padding="8,3" Margin="2,0" Click="Edit_Click" IsEnabled="False"/>
```

- [ ] **Step 5.2: Add AddMultiple_Click handler**

In `ScreensView.Viewer/Views/ComputersManagerWindow.xaml.cs`, after the `Add_Click` method, add:

```csharp
private void AddMultiple_Click(object sender, RoutedEventArgs e)
{
    var existingHosts = _mainVm.Computers
        .Select(c => c.Host)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    var win = new AddMultipleComputersWindow(existingHosts) { Owner = this };
    if (win.ShowDialog() == true && win.Results.Count > 0)
        _mainVm.AddComputers(win.Results);
}
```

Add `using System.Linq;` if not already present (it is via implicit usings, so likely not needed).

- [ ] **Step 5.3: Build the solution**

```bash
dotnet build
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 5.4: Run all tests**

```bash
dotnet test ScreensView.Tests
```

Expected: All tests pass.

- [ ] **Step 5.5: Commit**

```bash
git add ScreensView.Viewer/Views/ComputersManagerWindow.xaml ScreensView.Viewer/Views/ComputersManagerWindow.xaml.cs
git commit -m "feat: wire AddMultiple_Click into ComputersManagerWindow toolbar"
```
