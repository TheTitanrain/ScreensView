# Bulk Install Agents — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow installing/uninstalling the agent on multiple computers at once — both via multi-select in ComputersManagerWindow and via a prompt immediately after bulk-adding computers.

**Architecture:** Extract a testable `ComputerListHelpers.FormatNames` static helper; add `RemoveComputers` batch method to `MainViewModel` (with `SaveComputers` made virtual for testability); switch the list to multi-select; rewrite the five affected handlers.

**Tech Stack:** C# / WPF / xUnit / CommunityToolkit.Mvvm

---

## File Map

| File | Status | Purpose |
|---|---|---|
| `ScreensView.Viewer/Helpers/ComputerListHelpers.cs` | **New** | `internal static FormatNames(IEnumerable<string>) → string` |
| `ScreensView.Viewer/ViewModels/MainViewModel.cs` | Modify | Mark `SaveComputers` virtual; add `RemoveComputers` |
| `ScreensView.Viewer/Views/ComputersManagerWindow.xaml` | Modify | `SelectionMode="Extended"` |
| `ScreensView.Viewer/Views/ComputersManagerWindow.xaml.cs` | Modify | New helpers + rewrite 5 handlers |
| `ScreensView.Tests/ComputerListHelpersTests.cs` | **New** | 2 tests for `FormatNames` |
| `ScreensView.Tests/MainViewModelTests.cs` | Modify | 3 new tests for `RemoveComputers` |

---

### Task 1: `ComputerListHelpers` — new helper + tests

**Files:**
- Create: `ScreensView.Viewer/Helpers/ComputerListHelpers.cs`
- Create: `ScreensView.Tests/ComputerListHelpersTests.cs`

- [ ] **Step 1: Write failing tests**

Create `ScreensView.Tests/ComputerListHelpersTests.cs`:

```csharp
using ScreensView.Viewer.Helpers;

namespace ScreensView.Tests;

public class ComputerListHelpersTests
{
    [Fact]
    public void FormatNames_UpTo10_ReturnsAllJoined()
    {
        var names = new[] { "A", "B", "C", "D", "E" };
        Assert.Equal("A, B, C, D, E", ComputerListHelpers.FormatNames(names));
    }

    [Fact]
    public void FormatNames_Over10_TruncatesWithSuffix()
    {
        var names = Enumerable.Range(1, 12).Select(i => $"N{i}");
        Assert.Equal("N1, N2, N3, N4, N5, N6, N7, N8, N9, N10 и ещё 2",
            ComputerListHelpers.FormatNames(names));
    }
}
```

- [ ] **Step 2: Run tests — expect compile error (type not found)**

```bash
dotnet test ScreensView.Tests/ScreensView.Tests.csproj
```

Expected: build failure — `ComputerListHelpers` does not exist yet.

- [ ] **Step 3: Create `ComputerListHelpers`**

Create `ScreensView.Viewer/Helpers/ComputerListHelpers.cs`:

```csharp
namespace ScreensView.Viewer.Helpers;

internal static class ComputerListHelpers
{
    public static string FormatNames(IEnumerable<string> names)
    {
        var list = names.ToList();
        if (list.Count <= 10)
            return string.Join(", ", list);
        return string.Join(", ", list.Take(10)) + $" и ещё {list.Count - 10}";
    }
}
```

- [ ] **Step 4: Run tests — expect PASS**

```bash
dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter "FullyQualifiedName~ComputerListHelpersTests"
```

Expected: 2 tests pass.

- [ ] **Step 5: Commit**

```bash
git add ScreensView.Viewer/Helpers/ComputerListHelpers.cs ScreensView.Tests/ComputerListHelpersTests.cs
git commit -m "feat: add ComputerListHelpers.FormatNames with tests"
```

---

### Task 2: `MainViewModel` — `virtual SaveComputers` + `RemoveComputers` + tests

**Files:**
- Modify: `ScreensView.Viewer/ViewModels/MainViewModel.cs`
- Modify: `ScreensView.Tests/MainViewModelTests.cs`

- [ ] **Step 1: Write failing tests**

Add to `ScreensView.Tests/MainViewModelTests.cs` (after the existing tests):

```csharp
[Fact]
public void RemoveComputers_RemovesAllFromCollection()
{
    var vm = CreateVm();
    vm.AddComputers([
        new ComputerConfig { Name = "A", Host = "10.0.0.1", Port = 5443, ApiKey = "k1" },
        new ComputerConfig { Name = "B", Host = "10.0.0.2", Port = 5443, ApiKey = "k2" },
        new ComputerConfig { Name = "C", Host = "10.0.0.3", Port = 5443, ApiKey = "k3" },
    ]);
    var toRemove = vm.Computers.Take(2).ToList();

    vm.RemoveComputers(toRemove);

    Assert.Single(vm.Computers);
}

[Fact]
public void RemoveComputers_PersistsAfterReload()
{
    var storage = new ComputerStorageService(_tempFile);
    using (var vm = new MainViewModel(storage, new ScreenshotPollerService(new AgentHttpClient())))
    {
        vm.AddComputers([
            new ComputerConfig { Name = "A", Host = "10.0.0.1", Port = 5443, ApiKey = "k1" },
            new ComputerConfig { Name = "B", Host = "10.0.0.2", Port = 5443, ApiKey = "k2" },
            new ComputerConfig { Name = "C", Host = "10.0.0.3", Port = 5443, ApiKey = "k3" },
        ]);
        vm.RemoveComputers(vm.Computers.Take(2).ToList());
    }

    using var vm2 = new MainViewModel(storage, new ScreenshotPollerService(new AgentHttpClient()));
    Assert.Single(vm2.Computers);
}

[Fact]
public void RemoveComputers_SavesOnce()
{
    var storage = new ComputerStorageService(_tempFile);
    var vm = new CountingSaveViewModel(storage, new ScreenshotPollerService(new AgentHttpClient()));
    var configs = new[]
    {
        new ComputerConfig { Name = "A", Host = "10.0.0.1", Port = 5443, ApiKey = "k1" },
        new ComputerConfig { Name = "B", Host = "10.0.0.2", Port = 5443, ApiKey = "k2" },
        new ComputerConfig { Name = "C", Host = "10.0.0.3", Port = 5443, ApiKey = "k3" },
    };
    // Add directly to bypass SaveComputers (avoid counting setup saves)
    foreach (var c in configs)
        vm.Computers.Add(new ScreensView.Viewer.ViewModels.ComputerViewModel(c));

    vm.RemoveComputers(vm.Computers.ToList());

    Assert.Equal(1, vm.SaveCount);
}

private class CountingSaveViewModel(ComputerStorageService s, ScreenshotPollerService p)
    : MainViewModel(s, p)
{
    public int SaveCount { get; private set; }
    public override void SaveComputers() => SaveCount++;
}
```

- [ ] **Step 2: Run tests — expect failure**

```bash
dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter "FullyQualifiedName~RemoveComputers"
```

Expected: compile error — `RemoveComputers` not found, `SaveComputers` not virtual.

- [ ] **Step 3: Make `SaveComputers` virtual and add `RemoveComputers`**

In `ScreensView.Viewer/ViewModels/MainViewModel.cs`, make two changes:

1. Add `virtual` to `SaveComputers`:
```csharp
// Before:
public void SaveComputers()

// After:
public virtual void SaveComputers()
```

2. Add new method after `RemoveComputer`:
```csharp
public void RemoveComputers(IEnumerable<ComputerViewModel> vms)
{
    foreach (var vm in vms)
        Computers.Remove(vm);
    SaveComputers();
}
```

- [ ] **Step 4: Run all tests — expect PASS**

```bash
dotnet test ScreensView.Tests/ScreensView.Tests.csproj
```

Expected: all tests pass (existing + 3 new).

- [ ] **Step 5: Commit**

```bash
git add ScreensView.Viewer/ViewModels/MainViewModel.cs ScreensView.Tests/MainViewModelTests.cs
git commit -m "feat: add RemoveComputers batch method with save-once semantics"
```

---

### Task 3: XAML — enable multi-select

**Files:**
- Modify: `ScreensView.Viewer/Views/ComputersManagerWindow.xaml`

- [ ] **Step 1: Change `SelectionMode`**

In `ScreensView.Viewer/Views/ComputersManagerWindow.xaml`, find:

```xml
<ListView x:Name="ComputersList" SelectionMode="Single"
```

Replace with:

```xml
<ListView x:Name="ComputersList" SelectionMode="Extended"
```

- [ ] **Step 2: Build**

```bash
dotnet build ScreensView.Viewer/ScreensView.Viewer.csproj
```

Expected: build succeeds with no errors.

- [ ] **Step 3: Commit**

```bash
git add ScreensView.Viewer/Views/ComputersManagerWindow.xaml
git commit -m "feat: switch ComputersList to multi-select (Extended)"
```

---

### Task 4: Code-behind — rewrite handlers

**Files:**
- Modify: `ScreensView.Viewer/Views/ComputersManagerWindow.xaml.cs`

This task rewrites the code-behind completely. The full replacement content is shown below.

> **Note:** `Edit_Click` and `Selected` property are **unchanged**. All other handlers are rewritten.

- [ ] **Step 1: Replace the full `ComputersManagerWindow.xaml.cs` content**

```csharp
using System.Windows;
using System.Windows.Controls;
using ScreensView.Viewer.Helpers;

namespace ScreensView.Viewer.Views;

public partial class ComputersManagerWindow : Window
{
    private readonly ViewModels.MainViewModel _mainVm;

    public ComputersManagerWindow(ViewModels.MainViewModel mainVm)
    {
        InitializeComponent();
        _mainVm = mainVm;
        ComputersList.ItemsSource = mainVm.Computers;
    }

    private ViewModels.ComputerViewModel? Selected => ComputersList.SelectedItem as ViewModels.ComputerViewModel;

    private List<Shared.Models.ComputerConfig> SelectedConfigs =>
        ComputersList.SelectedItems.Cast<ViewModels.ComputerViewModel>()
            .Select(vm => vm.ToConfig()).ToList();

    private void ComputersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var count = ComputersList.SelectedItems.Count;
        BtnEdit.IsEnabled      = count == 1;
        BtnDelete.IsEnabled    = count >= 1;
        BtnInstall.IsEnabled   = count >= 1;
        BtnUninstall.IsEnabled = count >= 1;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var win = new AddEditComputerWindow(null) { Owner = this };
        if (win.ShowDialog() == true && win.Result != null)
            _mainVm.AddComputer(win.Result);
    }

    private void AddMultiple_Click(object sender, RoutedEventArgs e)
    {
        var existingHosts = _mainVm.Computers
            .Select(c => c.Host)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var win = new AddMultipleComputersWindow(existingHosts) { Owner = this };
        if (win.ShowDialog() != true || win.Results.Count == 0) return;

        var added = win.Results;
        _mainVm.AddComputers(added);

        if (MessageBox.Show($"Установить агент на {added.Count} добавленных компьютеров?",
                "Установка агента", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            LaunchInstall(added);
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Selected == null) return;
        var win = new AddEditComputerWindow(Selected.ToConfig()) { Owner = this };
        if (win.ShowDialog() == true && win.Result != null)
            _mainVm.UpdateComputer(Selected, win.Result);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var selected = ComputersList.SelectedItems.Cast<ViewModels.ComputerViewModel>().ToList();
        if (selected.Count == 0) return;

        var message = selected.Count == 1
            ? $"Удалить компьютер '{selected[0].Name}'?"
            : $"Удалить компьютеры: {ComputerListHelpers.FormatNames(selected.Select(vm => vm.Name))}?";

        if (MessageBox.Show(message, "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _mainVm.RemoveComputers(selected);
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        LaunchInstall(SelectedConfigs);
    }

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        var configs = SelectedConfigs;
        if (configs.Count == 0) return;

        var message = configs.Count == 1
            ? $"Удалить агент с '{configs[0].Name}'?"
            : $"Удалить агент с компьютеров: {ComputerListHelpers.FormatNames(configs.Select(c => c.Name))}?";

        if (MessageBox.Show(message, "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        LaunchUninstall(configs);
    }

    private void LaunchInstall(List<Shared.Models.ComputerConfig> configs)
    {
        var creds = new CredentialsDialog { Owner = this };
        if (creds.ShowDialog() != true) return;
        new InstallProgressWindow(InstallProgressWindow.Mode.Install, configs, creds.Username, creds.Password) { Owner = this }.ShowDialog();
    }

    private void LaunchUninstall(List<Shared.Models.ComputerConfig> configs)
    {
        var creds = new CredentialsDialog { Owner = this };
        if (creds.ShowDialog() != true) return;
        new InstallProgressWindow(InstallProgressWindow.Mode.Uninstall, configs, creds.Username, creds.Password) { Owner = this }.ShowDialog();
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build ScreensView.Viewer/ScreensView.Viewer.csproj
```

Expected: build succeeds with no errors.

- [ ] **Step 3: Run full test suite**

```bash
dotnet test
```

Expected: all tests pass.

- [ ] **Step 4: Commit**

```bash
git add ScreensView.Viewer/Views/ComputersManagerWindow.xaml.cs
git commit -m "feat: bulk install — multi-select handlers, post-add install prompt"
```
