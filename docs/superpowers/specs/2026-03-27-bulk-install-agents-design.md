# Bulk Install Agents — Design Spec

**Date:** 2026-03-27
**Status:** Approved

## Overview

Add the ability to install (or uninstall) the agent on multiple computers at once in `ComputersManagerWindow`. Two entry points: a prompt offered immediately after bulk-adding computers, and multi-select support in the computer list.

## UI Changes

### `ComputersManagerWindow.xaml`

Change `SelectionMode` from `Single` to `Extended` on `ComputersList`.

Button names in XAML (confirmed): `BtnAdd`, `BtnAddMultiple`, `BtnEdit`, `BtnDelete`, `BtnInstall`, `BtnUninstall`.

### Button enable/disable logic

| Button | Enabled when |
|---|---|
| Добавить | always |
| Добавить несколько | always |
| Редактировать | exactly 1 selected |
| Удалить | ≥ 1 selected |
| Установить агент | ≥ 1 selected |
| Удалить агент | ≥ 1 selected |

`BtnAdd` and `BtnAddMultiple` remain `IsEnabled="True"` in XAML and are never set in code. `SelectionChanged` updates the other four.

## Code Changes — `MainViewModel.cs`

### New `RemoveComputers` batch method

`RemoveComputer(vm)` calls `SaveComputers()` after every removal. For bulk delete, all VMs must be removed first and saved once.

```csharp
public void RemoveComputers(IEnumerable<ComputerViewModel> vms)
{
    foreach (var vm in vms)
        Computers.Remove(vm);
    SaveComputers();
}
```

`RemoveComputer(vm)` (single-item) is unchanged.

## Code Changes — `ComputersManagerWindow.xaml.cs`

### Keep `Selected` property

The existing `private ComputerViewModel? Selected => ComputersList.SelectedItem as ComputerViewModel` property is still used by the unchanged `Edit_Click`. Do not remove it.

### Name list helper

Confirmation dialogs truncate long selections: show all names joined with `, ` when count ≤ 10; when count > 10, show the first 10 joined with `, ` followed by ` и ещё N` (no comma before `и ещё`).

- 5 selected → `PC-01, PC-02, PC-03, PC-04, PC-05`
- 12 selected → `PC-01, PC-02, PC-03, PC-04, PC-05, PC-06, PC-07, PC-08, PC-09, PC-10 и ещё 2`

```csharp
private static string FormatNames(IEnumerable<string> names)
{
    var list = names.ToList();
    if (list.Count <= 10)
        return string.Join(", ", list);
    return string.Join(", ", list.Take(10)) + $" и ещё {list.Count - 10}";
}
```

### New helper property

```csharp
private List<ComputerConfig> SelectedConfigs =>
    ComputersList.SelectedItems.Cast<ComputerViewModel>().Select(vm => vm.ToConfig()).ToList();
```

### New helper methods

`InstallProgressWindow.Mode` enum already exists (values: `Install`, `Uninstall`, `UpdateAll`) — no changes to that file.

```csharp
private void LaunchInstall(List<ComputerConfig> configs)
{
    var creds = new CredentialsDialog { Owner = this };
    if (creds.ShowDialog() != true) return;
    new InstallProgressWindow(InstallProgressWindow.Mode.Install, configs, creds.Username, creds.Password) { Owner = this }.ShowDialog();
}

private void LaunchUninstall(List<ComputerConfig> configs)
{
    var creds = new CredentialsDialog { Owner = this };
    if (creds.ShowDialog() != true) return;
    new InstallProgressWindow(InstallProgressWindow.Mode.Uninstall, configs, creds.Username, creds.Password) { Owner = this }.ShowDialog();
}
```

`CredentialsDialog` is shown once per operation regardless of how many computers are selected.

### Modified handlers

**`SelectionChanged`** — updates four selection-dependent buttons:

```csharp
var count = ComputersList.SelectedItems.Count;
BtnEdit.IsEnabled      = count == 1;
BtnDelete.IsEnabled    = count >= 1;
BtnInstall.IsEnabled   = count >= 1;
BtnUninstall.IsEnabled = count >= 1;
```

**`Delete_Click`** — capture selection first; confirmation depends on count; delegates to `RemoveComputers` for one save:

- 1 selected: `«Удалить компьютер 'PC-01'?»`
- > 1 selected: `«Удалить компьютеры: PC-01, PC-02, PC-03, PC-04, PC-05?»` (via `FormatNames`; for > 10: `…, PC-10 и ещё 2?`)

```csharp
private void Delete_Click(object sender, RoutedEventArgs e)
{
    var selected = ComputersList.SelectedItems.Cast<ComputerViewModel>().ToList();
    if (selected.Count == 0) return;

    var message = selected.Count == 1
        ? $"Удалить компьютер '{selected[0].Name}'?"
        : $"Удалить компьютеры: {FormatNames(selected.Select(vm => vm.Name))}?";

    if (MessageBox.Show(message, "Подтверждение",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

    _mainVm.RemoveComputers(selected);
}
```

**`Install_Click`** — no confirmation dialog (install is non-destructive; credentials act as implicit confirmation). Button-disable logic is the sole zero-selection guard; no in-handler guard needed.

```csharp
LaunchInstall(SelectedConfigs);
```

**`Uninstall_Click`** — capture selection, show confirmation first, then credentials:

- 1 selected: `«Удалить агент с 'PC-01'?»`
- > 1 selected: `«Удалить агент с компьютеров: PC-01, PC-02, …?»` (via `FormatNames`; same truncation rule)

```csharp
private void Uninstall_Click(object sender, RoutedEventArgs e)
{
    var configs = SelectedConfigs;
    if (configs.Count == 0) return;

    var message = configs.Count == 1
        ? $"Удалить агент с '{configs[0].Name}'?"
        : $"Удалить агент с компьютеров: {FormatNames(configs.Select(c => c.Name))}?";

    if (MessageBox.Show(message, "Подтверждение",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

    LaunchUninstall(configs);
}
```

**`AddMultiple_Click`** — `win.Results` is `List<ComputerConfig>`. Capture it in a local variable before calling `AddComputers`:

```csharp
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
```

## Tests — `MainViewModelTests.cs`

Three new tests for `RemoveComputers`:

**`RemoveComputers_RemovesAllFromCollection`** — add 3, remove 2 by calling `RemoveComputers`, assert 1 remains.

**`RemoveComputers_PersistsAfterReload`** — add 3, remove 2, reload storage into a new VM, assert 1 survives.

**`RemoveComputers_SavesOnce`** — requires `SaveComputers()` to be `virtual` (see `MainViewModel` changes below). Test creates a `CountingSaveViewModel` subclass that overrides `SaveComputers()` to increment a counter; adds 3 computers, removes all 3 via `RemoveComputers`, asserts counter equals 1.

```csharp
private class CountingSaveViewModel(ComputerStorageService s, ScreenshotPollerService p)
    : MainViewModel(s, p)
{
    public int SaveCount { get; private set; }
    public override void SaveComputers() => SaveCount++;
}
```

> **Required `MainViewModel` change**: mark `SaveComputers()` as `virtual` — one additional word to the existing method declaration. No interface or other structural change needed.

## Tests — `ComputerListHelpersTests.cs` (unit, no WPF)

**`FormatNames_UpTo10_ReturnsAllJoined`** — pass 5 names, assert output equals `"A, B, C, D, E"`.

**`FormatNames_Over10_TruncatesWithSuffix`** — pass 12 names, assert output equals `"N1, N2, N3, N4, N5, N6, N7, N8, N9, N10 и ещё 2"`.

> `FormatNames` is `private static` — extract it to a new `internal static` helper class `ComputerListHelpers` in `ScreensView.Viewer/Helpers/` so it is testable without instantiating the window. `ComputersManagerWindow` calls `ComputerListHelpers.FormatNames(...)`.

## Files Affected

| File | Change |
|---|---|
| `Views/ComputersManagerWindow.xaml` | `SelectionMode="Extended"` |
| `Views/ComputersManagerWindow.xaml.cs` | Keep `Selected`; add `SelectedConfigs`, `LaunchInstall`, `LaunchUninstall`; update `SelectionChanged`, `Delete_Click`, `Install_Click`, `Uninstall_Click`, `AddMultiple_Click`; call `ComputerListHelpers.FormatNames` |
| `Helpers/ComputerListHelpers.cs` | **New** — `internal static FormatNames(IEnumerable<string>) → string` |
| `ViewModels/MainViewModel.cs` | Add `RemoveComputers(IEnumerable<ComputerViewModel>)` |
| `ScreensView.Tests/MainViewModelTests.cs` | Add 3 tests for `RemoveComputers` |
| `ScreensView.Tests/ComputerListHelpersTests.cs` | **New** — 2 tests for `FormatNames` |

No changes to `InstallProgressWindow`, `RemoteAgentInstaller`.

## Confirmed existing APIs

- `ComputerConfig.Name` — `string` property, exists in `ScreensView.Shared/Models/ComputerConfig.cs`
- `MainViewModel.AddComputers(IEnumerable<ComputerConfig>)` — exists, adds items and calls `SaveComputers()` once
- `MainViewModel.RemoveComputer(ComputerViewModel)` — exists (unchanged)
- `InstallProgressWindow.Mode` enum — exists with values `Install`, `Uninstall`, `UpdateAll`

## Notes

- **Stale selection after CredentialsDialog**: acceptable. `LaunchInstall`/`LaunchUninstall` receive the list captured before the dialog opens; they do not re-read the UI selection.
