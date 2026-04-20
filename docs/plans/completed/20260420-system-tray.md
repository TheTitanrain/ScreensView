# System Tray: Minimize to Tray on Close

## Overview

Add system tray support to ScreensView.Viewer. Clicking the window close button hides the app to the tray instead of exiting. A tray icon is always visible while the app runs. The user can restore the window, open settings, or exit via the tray context menu or by double-clicking the icon. A setting controls whether close minimizes to tray.

Spec: `docs/superpowers/specs/2026-04-20-system-tray-design.md`

## Context (from discovery)

- WPF .NET 8 app; settings persisted to `%AppData%\ScreensView\viewer-settings.json` via `ViewerSettings` / `ViewerSettingsService`
- `MainViewModel` uses CommunityToolkit.Mvvm `[ObservableProperty]` + `partial void On*Changed()` for all settings (see `Language`, `IsAutostartEnabled`)
- `SettingsWindow.DataContext` = `MainViewModel` (no separate settings VM)
- `LocalizationService` is static; hot-swaps language via `Switch()` → `Apply()`; no event today
- `screensview.ico` already included as `<Resource>` in csproj; accessible via `pack://application:,,,/screensview.ico`
- `MainWindow.xaml` wires `Closing="Window_Closing"`; `Window_Closing` calls `_vm.Dispose()` — must NOT fire during hide-to-tray
- Tests use `FakeViewerSettingsService` (projects specific fields in `Load()`/`Save()`) — must be updated for every new `ViewerSettings` field
- `App.OnStartup` wires all services; `MainWindow` constructed as `new MainWindow(viewModel, workflow)`

## Development Approach

- **Testing approach**: Regular (code first, then tests)
- Complete each task fully before moving to the next
- All tests must pass before starting the next task

## Testing Strategy

- Unit tests for: `LocalizationService.LanguageChanged` event, `ViewerSettings.MinimizeToTrayOnClose` persistence (including backward-compat: missing JSON key → defaults to `true`), `MainViewModel` load/save of the new property
- `TrayIconService` and `MainWindow.OnClosing` are WPF UI layer — covered by manual smoke testing in Post-Completion

## Solution Overview

Seven-step implementation following the spec:
1. Add NuGet dependency
2. Extend `LocalizationService` with `LanguageChanged` event
3. Extend `ViewerSettings` + `MainViewModel` with `MinimizeToTrayOnClose` + update `FakeViewerSettingsService`
4. Add localization strings + settings checkbox
5. Patch `MainWindow` with exit bypass (`_realClose` flag)
6. Create `TrayIconService`
7. Wire everything in `App.OnStartup`

## What Goes Where

**Implementation Steps** — all code changes below  
**Post-Completion** — manual smoke testing of tray behavior

---

## Implementation Steps

### Task 1: Add H.NotifyIcon.Wpf NuGet package

**Files:**
- Modify: `ScreensView.Viewer/ScreensView.Viewer.csproj`

- [ ] run `dotnet add ScreensView.Viewer package H.NotifyIcon.Wpf` to add the package reference
- [ ] run `dotnet build ScreensView.Viewer` — must succeed before task 2

---

### Task 2: Add `LanguageChanged` event to `LocalizationService`

**Files:**
- Modify: `ScreensView.Viewer/Services/LocalizationService.cs`
- Modify: `ScreensView.Tests/LocalizationServiceTests.cs`

- [ ] add `public static event Action? LanguageChanged;` to `LocalizationService`
- [ ] fire `LanguageChanged?.Invoke();` at the end of `Apply()`, after `SwapDictionary()`
- [ ] write test `Switch_FiresLanguageChangedEvent`: subscribe a handler before `Switch()`, verify it is called; wrap in try/finally to unsubscribe — prevents handler leak across tests
- [ ] run `dotnet test` — must pass before task 3

---

### Task 3: Add `MinimizeToTrayOnClose` to settings and view model

**Files:**
- Modify: `ScreensView.Viewer/Services/ViewerSettingsService.cs`
- Modify: `ScreensView.Viewer/ViewModels/MainViewModel.cs`
- Modify: `ScreensView.Tests/ViewerSettingsServiceTests.cs`
- Modify: `ScreensView.Tests/MainViewModelTests.cs`

- [ ] add `public bool MinimizeToTrayOnClose { get; set; } = true;` to `ViewerSettings`
- [ ] add `[ObservableProperty] private bool _minimizeToTrayOnClose;` to `MainViewModel`
- [ ] load `MinimizeToTrayOnClose` from `_viewerSettings` in the existing settings-loading block in `MainViewModel` constructor (same location as `_isAutostartEnabled`, `_language`, etc.)
- [ ] add `partial void OnMinimizeToTrayOnCloseChanged(bool value)`: `_viewerSettings.MinimizeToTrayOnClose = value; _viewerSettingsService.Save(_viewerSettings);`
- [ ] update `FakeViewerSettingsService` in `MainViewModelTests.cs`:
  - add `bool minimizeToTrayOnClose = true` constructor parameter
  - include `MinimizeToTrayOnClose` in `Current` initializer, `Load()` projection, and `Save()` capture
- [ ] write test `ViewerSettings_MinimizeToTrayOnClose_DefaultsToTrue`: `new ViewerSettings().MinimizeToTrayOnClose == true`
- [ ] write test `ViewerSettingsService_Persists_MinimizeToTrayOnClose`: save `false`, reload, assert `false`
- [ ] write test `Load_WhenMinimizeToTrayOnCloseFieldIsMissing_ReturnsTrue`: write JSON without `MinimizeToTrayOnClose` key, load, assert `true` — backward-compat for existing user settings files (follow `Load_WhenExternalSourceFieldsAreMissing_ReturnsDefaultValues` pattern at `ViewerSettingsServiceTests.cs:46`)
- [ ] write test `MainViewModel_LoadsMinimizeToTrayOnCloseFromSettings`: settings with `minimizeToTrayOnClose: false` → `vm.MinimizeToTrayOnClose == false`
- [ ] write test `MainViewModel_SavesMinimizeToTrayOnCloseOnChange`: change property → `settingsService.Current.MinimizeToTrayOnClose` reflects new value and `SaveCalls` incremented
- [ ] run `dotnet test` — must pass before task 4

---

### Task 4: Localization strings + settings checkbox

**Files:**
- Modify: `ScreensView.Viewer/Resources/Strings.en.xaml`
- Modify: `ScreensView.Viewer/Resources/Strings.ru.xaml`
- Modify: `ScreensView.Viewer/Views/SettingsWindow.xaml`

- [ ] add to `Strings.en.xaml` (in the Settings section):
  ```xml
  <sys:String x:Key="Str.Settings.MinimizeToTray">Minimize to tray on close</sys:String>
  <sys:String x:Key="Str.Tray.Show">Show</sys:String>
  <sys:String x:Key="Str.Tray.Hide">Hide</sys:String>
  <sys:String x:Key="Str.Tray.OpenSettings">Open Settings</sys:String>
  <sys:String x:Key="Str.Tray.Exit">Exit</sys:String>
  ```
- [ ] add matching keys to `Strings.ru.xaml`:
  ```xml
  <sys:String x:Key="Str.Settings.MinimizeToTray">Сворачивать в трей при закрытии</sys:String>
  <sys:String x:Key="Str.Tray.Show">Показать</sys:String>
  <sys:String x:Key="Str.Tray.Hide">Скрыть</sys:String>
  <sys:String x:Key="Str.Tray.OpenSettings">Настройки</sys:String>
  <sys:String x:Key="Str.Tray.Exit">Выход</sys:String>
  ```
- [ ] add checkbox to `SettingsWindow.xaml` General section, after the Autostart `CheckBox` (currently line 62):
  ```xml
  <CheckBox IsChecked="{Binding MinimizeToTrayOnClose}"
            Content="{DynamicResource Str.Settings.MinimizeToTray}"
            Margin="0,8,0,0"/>
  ```
- [ ] run `dotnet build ScreensView.Viewer` — must succeed before task 5

---

### Task 5: Patch `MainWindow` with exit bypass

**Files:**
- Modify: `ScreensView.Viewer/MainWindow.xaml.cs`

- [ ] add `private bool _realClose;` field to `MainWindow`
- [ ] add `internal void RequestRealClose() => _realClose = true;`
- [ ] add `using System.ComponentModel;` if not already present
- [ ] override `OnClosing`:
  ```csharp
  protected override void OnClosing(CancelEventArgs e)
  {
      if (!_realClose && _vm.MinimizeToTrayOnClose)
      {
          e.Cancel = true;
          Hide();
          return;
          // Intentionally NOT calling base.OnClosing(e):
          // base raises the Closing event, which would invoke Window_Closing
          // and call _vm.Dispose() — must not happen during hide-to-tray.
      }
      base.OnClosing(e); // real close: raises Closing → Window_Closing → _vm.Dispose()
  }
  ```
- [ ] run `dotnet build ScreensView.Viewer` — must succeed before task 6

---

### Task 6: Create `TrayIconService`

**Files:**
- Create: `ScreensView.Viewer/Services/TrayIconService.cs`

- [ ] create `internal sealed class TrayIconService : IDisposable`
- [ ] constructor: `(MainWindow mainWindow, MainViewModel vm, Action onOpenSettings)`
- [ ] store fields: `_mainWindow`, `_onOpenSettings`, `_taskbarIcon`, `_showHideItem`, `_openSettingsItem`, `_exitItem` (all localized items stored so `RefreshMenuLabels()` can update them all)
- [ ] initialize `TaskbarIcon`:
  - `IconSource = new BitmapImage(new Uri("pack://application:,,,/screensview.ico"))`
  - `ToolTipText = "ScreensView"` (hardcoded, does not change with language)
- [ ] build `ContextMenu` programmatically:
  - `_showHideItem = new MenuItem()` — header set in `RefreshMenuLabels()`
  - `_showHideItem.Click` → if `_mainWindow.IsVisible` then `_mainWindow.Hide()` else `{ _mainWindow.Show(); _mainWindow.Activate(); }`
  - `_openSettingsItem = new MenuItem()` — header set in `RefreshMenuLabels()`; `Click` → `_onOpenSettings()`
  - `new Separator()`
  - `_exitItem = new MenuItem()` — header set in `RefreshMenuLabels()`; `Click` → `{ _mainWindow.RequestRealClose(); Application.Current.Shutdown(); }`
  - assign `_taskbarIcon.ContextMenu = new ContextMenu { Items = { _showHideItem, _openSettingsItem, new Separator(), _exitItem } }`
- [ ] subscribe `_taskbarIcon.TrayMouseDoubleClick += (_, _) => { _mainWindow.Show(); _mainWindow.Activate(); };`
- [ ] implement `RefreshMenuLabels()`: updates headers on `_showHideItem`, `_openSettingsItem`, and `_exitItem` via `LocalizationService.Get(key)`; `_showHideItem.Header` = `Str.Tray.Hide` when visible, `Str.Tray.Show` when hidden
- [ ] add named handler field `_onIsVisibleChanged` for `IsVisibleChanged` event, subscribe in constructor; use named field (not lambda) so `Dispose()` can unsubscribe
- [ ] add named handler field `_onLanguageChanged` for `LocalizationService.LanguageChanged` event, subscribe in constructor; use named field (not lambda) for proper unsubscription
- [ ] call `RefreshMenuLabels()` at end of constructor to set initial labels
- [ ] implement `Dispose()`: unsubscribe `_mainWindow.IsVisibleChanged -= _onIsVisibleChanged`, unsubscribe `LocalizationService.LanguageChanged -= _onLanguageChanged`, call `_taskbarIcon.Dispose()`
- [ ] run `dotnet build ScreensView.Viewer` — must succeed before task 7

---

### Task 7: Wire `TrayIconService` in `App.OnStartup`

**Files:**
- Modify: `ScreensView.Viewer/App.xaml.cs`

- [ ] after `mainWindow.Show()`, add:
  ```csharp
  var trayService = new TrayIconService(
      mainWindow,
      viewModel,
      onOpenSettings: () => new SettingsWindow(viewModel, workflow) { Owner = mainWindow }.ShowDialog());
  Application.Current.Exit += (_, _) => trayService.Dispose();
  ```
- [ ] run `dotnet build` — full solution build must succeed
- [ ] run `dotnet test` — all tests must pass

---

### Task 8: Verify acceptance criteria

- [ ] run `dotnet test` — all tests pass with no regressions
- [ ] verify `ViewerSettings.MinimizeToTrayOnClose` defaults to `true`
- [ ] verify `LocalizationService.LanguageChanged` fires on `Switch()`
- [ ] verify `MainViewModel` load/save round-trip for `MinimizeToTrayOnClose`

---

### Task 9: Update documentation and move plan

**Files:**
- Modify: `README.md`
- Modify: `README.ru.md`

- [ ] add brief mention of tray icon behavior to README.md (feature list or usage section)
- [ ] add matching line to README.ru.md
- [ ] run `mkdir -p docs/plans/completed && mv docs/plans/20260420-system-tray.md docs/plans/completed/`

---

## Post-Completion

**Manual smoke testing:**
- Launch app → tray icon appears in system tray
- Click window X → window hides, polling continues (verify tray icon still present, no crash)
- Double-click tray icon → window restores and gains focus
- Right-click tray: "Show" label shown when window hidden, "Hide" when visible
- Right-click tray → "Open Settings" → settings window opens as dialog
- Right-click tray → "Exit" → app terminates cleanly
- Settings: uncheck "Minimize to tray on close" → clicking X now closes app normally
- Switch language in Settings → tray menu labels update immediately (all three items)
- Relaunch → `MinimizeToTrayOnClose` preference is preserved
