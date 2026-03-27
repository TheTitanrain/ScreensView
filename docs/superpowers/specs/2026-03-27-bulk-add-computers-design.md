# Bulk Add Computers — Design Spec

**Date:** 2026-03-27
**Status:** Approved

## Overview

Add functionality to add multiple computers at once in `ComputersManagerWindow`. Two input modes: by host list (one per line) and by IP range. Each added computer gets a unique auto-generated API key; the name equals the host/IP value.

## UI Changes

### ComputersManagerWindow toolbar

Add a new button **«Добавить несколько»** (`BtnAddMultiple`) immediately after the existing **«Добавить»** button. Button is always enabled (no selection required).

### New window: AddMultipleComputersWindow

Single dialog (`Height=380`, `Width=460`) with a `TabControl` containing two tabs.

**Tab 1 — По хостам (By hosts)**

- `TextBox` (multiline, `AcceptsReturn=True`) — one host or IP per line; blank lines are ignored
- `TextBox` for port (default: `Constants.DefaultPort`)
- Read-only note: "API-ключ генерируется автоматически для каждого"
- Live counter label: "N компьютеров будет добавлено" (updates on text change)

**Tab 2 — По диапазону IP (By IP range)**

- `TextBox` for start IP
- `TextBox` for end IP
- `TextBox` for port (default: `Constants.DefaultPort`)
- Read-only note: same as above
- Live counter/preview label: "N компьютеров будет добавлено (x.x.x.x … x.x.x.x)" — updates on input change; shows error text if IPs are invalid or range exceeds 256

**Buttons (shared footer)**

- **Добавить (N)** — `IsDefault=True`; N reflects the current count from the active tab
- **Отмена** — `IsCancel=True`

## Data Contract

Public property on `AddMultipleComputersWindow`:

```csharp
public List<ComputerConfig> Results { get; private set; }
```

Each `ComputerConfig` in `Results`:

| Field | Value |
|---|---|
| `Id` | `Guid.NewGuid()` |
| `Name` | host string (trimmed) |
| `Host` | host string (trimmed) |
| `Port` | value from port field |
| `ApiKey` | `GenerateApiKey()` — same method as in `AddEditComputerWindow` |
| `IsEnabled` | `true` |
| `CertThumbprint` | `""` |

## Parsing Logic

### By hosts

```
lines = TextBox.Text.Split('\n')
hosts = lines.Select(l => l.Trim()).Where(l => l != "")
```

Each non-empty trimmed line becomes one `ComputerConfig`. No further format validation — invalid hostnames will simply fail to connect at poll time (same as single-add).

### By IP range

1. Parse `startIp` and `endIp` via `IPAddress.Parse()` — show error label if either is invalid.
2. Convert both to `uint` (big-endian byte order).
3. If `endUint < startUint` — show error "Конечный IP меньше начального".
4. If `endUint - startUint >= 256` — show error "Диапазон не должен превышать 255 адресов".
5. Iterate `uint` from `startUint` to `endUint` inclusive; convert each back to `IPAddress`, use `.ToString()` as both `Name` and `Host`.

## Validation

- Port: `int.TryParse` + range 1–65535; show `MessageBox` on OK click if invalid.
- Hosts tab: at least one non-empty line required; show `MessageBox` on OK click if zero hosts.
- IP range tab: errors shown inline (label) as user types; OK button disabled when errors exist.

## Integration

`ComputersManagerWindow.AddMultiple_Click`:

```csharp
private void AddMultiple_Click(object sender, RoutedEventArgs e)
{
    var win = new AddMultipleComputersWindow { Owner = this };
    if (win.ShowDialog() == true)
        foreach (var config in win.Results)
            _mainVm.AddComputer(config);
}
```

`MainViewModel.AddComputer` is called once per result — no changes to `MainViewModel` required.

## Files Affected

| File | Change |
|---|---|
| `Views/AddMultipleComputersWindow.xaml` | **New** |
| `Views/AddMultipleComputersWindow.xaml.cs` | **New** |
| `Views/ComputersManagerWindow.xaml` | Add `BtnAddMultiple` button to toolbar |
| `Views/ComputersManagerWindow.xaml.cs` | Add `AddMultiple_Click` handler + wire button |

No changes to `MainViewModel`, `ComputerStorageService`, shared models, or agent projects.
