# System Tray: Minimize to Tray on Close

**Date:** 2026-04-20  
**Status:** Approved

## Goal

When the user clicks the window close button, ScreensView.Viewer hides to the system tray instead of exiting. A tray icon is always visible while the app is running. The user can restore the window, open settings, or exit via the tray icon context menu or by double-clicking the icon.

## NuGet Dependency

Add `H.NotifyIcon.Wpf` to `ScreensView.Viewer.csproj`. This package provides a WPF-native `TaskbarIcon` control with real WPF `ContextMenu` support, compatible with .NET 8.

## Components

### 1. `TrayIconService` (new — `Services/TrayIconService.cs`)

Owns and manages the `TaskbarIcon` instance. Created once at app startup, disposed on real shutdown.

Responsibilities:
- Initialize `TaskbarIcon` with `screensview.ico` and tooltip "ScreensView"
- Build WPF `ContextMenu` programmatically (to support dynamic label and localization)
- Handle double-click → show + activate `MainWindow`
- Subscribe to `mainWindow.IsVisibleChanged` to auto-update Show/Hide label — no callback needed from `MainWindow`
- Accept `Action onOpenSettings` callback (set in `App.xaml.cs`) for the Open Settings menu item — keeps `TrayIconService` decoupled from `SettingsWindow` and its dependencies
- Handle Exit menu item → `Application.Current.Shutdown()`
- `Dispose()` → `TaskbarIcon.Dispose()`

Context menu items (in order):
1. **Show / Hide** — dynamic: "Show" when `MainWindow` is not visible, "Hide" when visible. Clicking Show: `window.Show(); window.Activate();`. Clicking Hide: `window.Hide()`.
2. **Open Settings** — opens `SettingsWindow` as a dialog
3. `Separator`
4. **Exit** — `Application.Current.Shutdown()`

### 2. `App.xaml.cs` (modified)

- Set `ShutdownMode = ShutdownMode.OnExplicitShutdown` on startup, before creating `MainWindow`. This prevents the process from exiting when `MainWindow` is hidden.
- Create `TrayIconService` after `MainWindow` is created (service needs window reference).
- On `Exit` event: `trayIconService.Dispose()`.

### 3. `MainWindow` — `OnClosing` override (modified)

```csharp
protected override void OnClosing(CancelEventArgs e)
{
    if (_settings.MinimizeToTrayOnClose)
    {
        e.Cancel = true;
        Hide();
        _trayIconService.UpdateMenuLabels();
    }
    else
    {
        base.OnClosing(e);
    }
}
```

`MainWindow` receives `ViewerSettings` (or reads it fresh from `ViewerSettingsService`) and `TrayIconService` via constructor injection, consistent with existing pattern.

### 4. `ViewerSettings` (modified — `Services/ViewerSettingsService.cs`)

Add one property:

```csharp
public bool MinimizeToTrayOnClose { get; set; } = true;
```

Default `true`: first-run users get tray behavior without extra configuration.

### 5. `SettingsWindow` + `SettingsViewModel` (modified)

Add checkbox in the **General** section (after the Autostart checkbox):

```xml
<CheckBox IsChecked="{Binding MinimizeToTrayOnClose}"
          Content="{DynamicResource Str.Settings.MinimizeToTray}"/>
```

`SettingsViewModel` gets a `MinimizeToTrayOnClose` bool property, read from and saved to `ViewerSettings`.

### 6. Localization strings (new keys)

**`Strings.en.xaml`:**
```xml
<sys:String x:Key="Str.Settings.MinimizeToTray">Minimize to tray on close</sys:String>
<sys:String x:Key="Str.Tray.Show">Show</sys:String>
<sys:String x:Key="Str.Tray.Hide">Hide</sys:String>
<sys:String x:Key="Str.Tray.OpenSettings">Open Settings</sys:String>
<sys:String x:Key="Str.Tray.Exit">Exit</sys:String>
<sys:String x:Key="Str.Tray.Tooltip">ScreensView</sys:String>
```

**`Strings.ru.xaml`:**
```xml
<sys:String x:Key="Str.Settings.MinimizeToTray">Сворачивать в трей при закрытии</sys:String>
<sys:String x:Key="Str.Tray.Show">Показать</sys:String>
<sys:String x:Key="Str.Tray.Hide">Скрыть</sys:String>
<sys:String x:Key="Str.Tray.OpenSettings">Настройки</sys:String>
<sys:String x:Key="Str.Tray.Exit">Выход</sys:String>
<sys:String x:Key="Str.Tray.Tooltip">ScreensView</sys:String>
```

Localization read via existing `LocalizationService.Get(key)` pattern.

## Data Flow

```
App startup
  → ShutdownMode = OnExplicitShutdown
  → new MainWindow(...)
  → new TrayIconService(mainWindow, settingsService)
    → TaskbarIcon created, icon = screensview.ico

User clicks X on MainWindow
  → MainWindow.OnClosing
  → MinimizeToTrayOnClose == true → e.Cancel = true, window.Hide()
  → TrayIconService.UpdateMenuLabels() → menu item text = "Show"

User double-clicks tray icon
  → TrayIconService → window.Show(), window.Activate()
  → TrayIconService.UpdateMenuLabels() → menu item text = "Hide"

User right-clicks tray → "Exit"
  → Application.Current.Shutdown()
  → App.Exit event → TrayIconService.Dispose()
```

## What Is Not Changing

- No "start minimized" option (not requested)
- No balloon notification on first minimize (not requested)
- Polling service continues running when window is hidden (existing behavior, no change needed)
- `ShutdownMode` change is the only App-level change; all other startup logic stays as-is
