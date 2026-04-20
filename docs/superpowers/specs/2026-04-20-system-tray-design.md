# System Tray: Minimize to Tray on Close

**Date:** 2026-04-20  
**Status:** Approved (rev 2)

## Goal

When the user clicks the window close button, ScreensView.Viewer hides to the system tray instead of exiting. A tray icon is always visible while the app is running. The user can restore the window, open settings, or exit via the tray icon context menu or by double-clicking the icon.

## NuGet Dependency

Add `H.NotifyIcon.Wpf` to `ScreensView.Viewer.csproj`. This package provides a WPF-native `TaskbarIcon` control with real WPF `ContextMenu` support, compatible with .NET 8.

## Components

### 1. `TrayIconService` (new — `Services/TrayIconService.cs`)

Owns and manages the `TaskbarIcon` instance. Created in `App.OnStartup` **after** `MainWindow` is constructed. Disposed when the app exits.

Constructor signature:

```csharp
internal TrayIconService(MainWindow mainWindow, MainViewModel vm, Action onOpenSettings)
```

- `mainWindow` — for `Show()` / `Hide()` / `Activate()`
- `vm` — for reading `MinimizeToTrayOnClose` from settings (via existing property on `MainViewModel`)
- `onOpenSettings` — callback set up in `App.OnStartup`; decouples `TrayIconService` from `SettingsWindow` and its dependencies

Responsibilities:

- Initialize `TaskbarIcon` with `screensview.ico` and tooltip `"ScreensView"`
- Build WPF `ContextMenu` programmatically using `LocalizationService.Get(key)` at construction time
- Subscribe to `mainWindow.IsVisibleChanged` → call `RefreshMenuLabels()` to update Show/Hide item text
- Subscribe to `LocalizationService.LanguageChanged` static event → call `RefreshMenuLabels()` to re-read localized strings after language switch
- Double-click → `mainWindow.Show(); mainWindow.Activate();`
- `Dispose()` → `TaskbarIcon.Dispose()`; unsubscribe events

Context menu items (in order):

1. **Show / Hide** — dynamic label: `Str.Tray.Show` when window not visible, `Str.Tray.Hide` when visible. Clicking Show → `mainWindow.Show(); mainWindow.Activate()`. Clicking Hide → `mainWindow.Hide()`.
2. **Open Settings** — invokes `onOpenSettings` callback
3. `Separator`
4. **Exit** — calls `mainWindow.RequestRealClose()`, then `Application.Current.Shutdown()`

### 2. `MainWindow` — changes to `OnClosing` and exit bypass

Add a `bool _realClose` field. Add internal method:

```csharp
internal void RequestRealClose()
{
    _realClose = true;
}
```

Override `OnClosing`:

```csharp
protected override void OnClosing(CancelEventArgs e)
{
    if (!_realClose && _vm.MinimizeToTrayOnClose)
    {
        e.Cancel = true;
        Hide();
        return;
    }
    base.OnClosing(e);
}
```

This is the **only** place `MinimizeToTrayOnClose` controls behavior. When `_realClose` is true (set by `TrayIconService.Exit`), the close proceeds normally and `Application.Current.Shutdown()` terminates the process. When `MinimizeToTrayOnClose` is false, the close button behaves as before.

`Application.Current.Shutdown()` fires the `Application.Exit` event and calls `Close()` on open windows; `_realClose = true` ensures `OnClosing` does not re-cancel. Existing shutdown calls in `ViewerUpdateService` (lines 59, 218) fire before `MainWindow` is created, so they are unaffected.

`ShutdownMode` is **not changed**. Canceling `OnClosing` keeps `MainWindow` alive, so `OnLastWindowClose` (the default) never fires while the user is hiding-to-tray. No global ShutdownMode change needed.

`MainWindow` constructor adds `TrayIconService` via **setter injection** after construction, to avoid the circular dependency (App creates MainWindow, then creates TrayIconService with mainWindow reference):

```csharp
// In App.OnStartup, after mainWindow and trayService are both created:
// trayService is disposable; App.Exit disposes it.
// MainWindow needs no reference to TrayIconService directly.
```

`MainWindow` does not receive `TrayIconService` in its constructor. The only coupling is that `TrayIconService` calls `mainWindow.RequestRealClose()` before `Shutdown()`.

### 3. `LocalizationService` — add `LanguageChanged` event (modified)

Add one static event fired at the end of `Apply()`:

```csharp
public static event Action? LanguageChanged;
```

Fire in `Apply()` after `SwapDictionary()`:

```csharp
LanguageChanged?.Invoke();
```

`TrayIconService` subscribes on construction, unsubscribes on dispose.

### 4. `ViewerSettings` + `MainViewModel` (modified)

**`ViewerSettings`** — add property:

```csharp
public bool MinimizeToTrayOnClose { get; set; } = true;
```

Default `true`: first-run users get tray behavior without extra configuration.

**`MainViewModel`** — add observable property (same pattern as `IsAutostartEnabled`, `Language`):

```csharp
[ObservableProperty] private bool _minimizeToTrayOnClose;
```

Load from settings in the existing settings-loading block. Save when changed (same pattern as `OnIsAutostartEnabledChanged`).

### 5. `SettingsWindow.xaml` (modified)

Add checkbox in the **General** section, after the Autostart checkbox (line 62 in current file). Binds to `MainViewModel.MinimizeToTrayOnClose` via `DataContext = _vm` (already set):

```xml
<CheckBox IsChecked="{Binding MinimizeToTrayOnClose}"
          Content="{DynamicResource Str.Settings.MinimizeToTray}"
          Margin="0,8,0,0"/>
```

No new ViewModel needed. `SettingsWindow.DataContext` is already `MainViewModel`.

### 6. `App.OnStartup` (modified)

After `mainWindow = new MainWindow(...)` and `mainWindow.Show()`:

```csharp
var trayService = new TrayIconService(
    mainWindow,
    viewModel,
    onOpenSettings: () => new SettingsWindow(viewModel, workflow) { Owner = mainWindow }.ShowDialog());

Application.Current.Exit += (_, _) => trayService.Dispose();
```

No change to `ShutdownMode`. `StartupShutdownScope` remains unchanged — it is `using`-scoped around `CheckAndUpdateAsync()` only, and disposes before `MainWindow` is shown.

### 7. Localization strings (new keys)

**`Strings.en.xaml`:**

```xml
<sys:String x:Key="Str.Settings.MinimizeToTray">Minimize to tray on close</sys:String>
<sys:String x:Key="Str.Tray.Show">Show</sys:String>
<sys:String x:Key="Str.Tray.Hide">Hide</sys:String>
<sys:String x:Key="Str.Tray.OpenSettings">Open Settings</sys:String>
<sys:String x:Key="Str.Tray.Exit">Exit</sys:String>
```

**`Strings.ru.xaml`:**

```xml
<sys:String x:Key="Str.Settings.MinimizeToTray">Сворачивать в трей при закрытии</sys:String>
<sys:String x:Key="Str.Tray.Show">Показать</sys:String>
<sys:String x:Key="Str.Tray.Hide">Скрыть</sys:String>
<sys:String x:Key="Str.Tray.OpenSettings">Настройки</sys:String>
<sys:String x:Key="Str.Tray.Exit">Выход</sys:String>
```

Tooltip text (`"ScreensView"`) is hardcoded — it does not change with language.

## Data Flow

```
App.OnStartup
  → StartupShutdownScope (temporary, disposes before Show)
  → MainWindow created and shown (ShutdownMode stays OnLastWindowClose)
  → TrayIconService created: TaskbarIcon visible, subscribes IsVisibleChanged + LanguageChanged

User clicks X on MainWindow
  → OnClosing: _realClose=false, MinimizeToTrayOnClose=true → e.Cancel=true, Hide()
  → IsVisibleChanged fires → TrayIconService.RefreshMenuLabels() → item = "Show"

User double-clicks tray icon
  → mainWindow.Show(); mainWindow.Activate()
  → IsVisibleChanged fires → item = "Hide"

User right-clicks tray → "Exit"
  → mainWindow.RequestRealClose() → _realClose=true
  → Application.Current.Shutdown()
  → OnClosing fires: _realClose=true → normal close, no cancel
  → App.Exit event → trayService.Dispose()

User switches language in Settings
  → MainViewModel.Language changes → LocalizationService.Switch()
  → LocalizationService.LanguageChanged fires
  → TrayIconService.RefreshMenuLabels() → re-reads strings via LocalizationService.Get()
```

## What Is Not Changing

- No "start minimized" option (not requested)
- No balloon notification on first minimize (not requested)
- Polling continues when window is hidden (no change)
- `ShutdownMode` stays default `OnLastWindowClose`
- `StartupShutdownScope` in `App.xaml.cs` is untouched
- `ViewerUpdateService` shutdown paths (lines 59, 218) unaffected — they run before `MainWindow` exists
