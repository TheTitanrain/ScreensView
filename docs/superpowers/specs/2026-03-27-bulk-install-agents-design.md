# Bulk Install Agents — Design Spec

**Date:** 2026-03-27
**Status:** Approved

## Overview

Add the ability to install (or uninstall) the agent on multiple computers at once in `ComputersManagerWindow`. Two entry points: a prompt offered immediately after bulk-adding computers, and multi-select support in the computer list.

## UI Changes

### `ComputersManagerWindow.xaml`

Change `SelectionMode` from `Single` to `Extended` on `ComputersList`.

### Button enable/disable logic

| Button | Enabled when |
|---|---|
| Добавить | always |
| Добавить несколько | always |
| Редактировать | exactly 1 selected |
| Удалить | ≥ 1 selected |
| Установить агент | ≥ 1 selected |
| Удалить агент | ≥ 1 selected |

`SelectionChanged` updates all six buttons based on `SelectedItems.Count`.

## Code Changes — `ComputersManagerWindow.xaml.cs`

### New helper property

```csharp
private List<ComputerConfig> SelectedConfigs =>
    ComputersList.SelectedItems.Cast<ComputerViewModel>().Select(vm => vm.ToConfig()).ToList();
```

### New helper methods

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

**`SelectionChanged`** — updates all six buttons:
```csharp
BtnEdit.IsEnabled    = count == 1;
BtnDelete.IsEnabled  = count >= 1;
BtnInstall.IsEnabled = count >= 1;
BtnUninstall.IsEnabled = count >= 1;
```

**`Delete_Click`** — confirmation message depends on count:
- 1 selected: existing behavior — `«Удалить компьютер 'PC-01'?»`
- > 1 selected: `«Удалить компьютеры: PC-01, PC-02, PC-03?»` (names joined with `, `)

**`Install_Click`** — simplified to:
```csharp
LaunchInstall(SelectedConfigs);
```

**`Uninstall_Click`** — confirmation with names list (same pattern as Delete), then:
```csharp
LaunchUninstall(SelectedConfigs);
```

**`AddMultiple_Click`** — after computers are added, offer install:
```csharp
if (win.ShowDialog() == true && win.Results.Count > 0)
{
    _mainVm.AddComputers(win.Results);
    if (MessageBox.Show($"Установить агент на {win.Results.Count} добавленных компьютеров?",
            "Установка агента", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        LaunchInstall(win.Results);
}
```

## Files Affected

| File | Change |
|---|---|
| `Views/ComputersManagerWindow.xaml` | `SelectionMode="Extended"` |
| `Views/ComputersManagerWindow.xaml.cs` | `SelectionChanged`, `Delete_Click`, `Install_Click`, `Uninstall_Click`, `AddMultiple_Click` + `SelectedConfigs` + `LaunchInstall` + `LaunchUninstall` |

No changes to `InstallProgressWindow`, `RemoteAgentInstaller`, `MainViewModel`, or tests.
