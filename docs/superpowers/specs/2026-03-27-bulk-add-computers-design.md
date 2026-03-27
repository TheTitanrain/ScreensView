# Bulk Add Computers — Design Spec

**Date:** 2026-03-27
**Status:** Approved

## Overview

Add functionality to add multiple computers at once in `ComputersManagerWindow`. Two input modes: by host list (one per line) and by IP range. Each added computer gets a unique auto-generated API key; the name equals the host/IP value. Duplicate hosts (already present in the collection) are silently skipped.

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
- Live counter/preview label: "N компьютеров будет добавлено (x.x.x.x … x.x.x.x)" — updates on input change; shows inline error text when IPs are invalid, non-IPv4, or range exceeds 255 addresses

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
| `ApiKey` | `BulkComputerParser.GenerateApiKey()` |
| `IsEnabled` | `true` |
| `CertThumbprint` | `""` |

## Parsing Logic

All parsing logic lives in a new static class `BulkComputerParser` (in `ScreensView.Viewer/Services/BulkComputerParser.cs`). The window's code-behind calls it; no parsing logic lives in the XAML code-behind.

`BulkComputerParser` exposes:
- `ParseHosts(string text, int port) → IReadOnlyList<ComputerConfig>`
- `ParseIpRange(string startText, string endText, int port, out string? error) → IReadOnlyList<ComputerConfig>`
- `GenerateApiKey() → string` (extracted from `AddEditComputerWindow`, same implementation: 32 random bytes as lowercase hex)

### By hosts (`ParseHosts`)

```
lines = text.Split('\n')
hosts = lines.Select(l => l.Trim()).Where(l => l != "")
```

Each non-empty trimmed line becomes one `ComputerConfig`.
 No further format validation — invalid hostnames will simply fail to connect at poll time (same as single-add).

### By IP range (`ParseIpRange`)

1. Parse `startText` and `endText` via `IPAddress.TryParse()`.
2. If either fails to parse — return `error = "Некорректный IP-адрес"`, empty list.
3. If either address is not `AddressFamily.InterNetwork` (i.e. IPv6) — return `error = "Поддерживается только IPv4"`, empty list.
4. Convert both to `uint` via big-endian byte order (`GetAddressBytes()`).
5. If `endUint < startUint` — return `error = "Конечный IP меньше начального"`, empty list.
6. If `endUint - startUint >= 255` — return `error = "Диапазон не должен превышать 255 адресов"`, empty list.
7. Iterate `uint` from `startUint` to `endUint` inclusive; convert each back to `IPAddress`, use `.ToString()` as both `Name` and `Host`.

> **Boundary**: maximum 255 addresses (when `endUint - startUint == 254`, count = 255). The condition `>= 255` rejects 255 or more addresses.

## Validation

- Port: `int.TryParse` + range 1–65535; show `MessageBox` on OK click if invalid (both tabs).
- Hosts tab: at least one non-empty line required; show `MessageBox` on OK click if zero hosts.
- IP range tab: errors from `ParseIpRange` shown inline (label) as user types; **Добавить** button disabled when `error != null`. Port validity on this tab is checked only on OK click (same as hosts tab) — the button-disabled state is tied exclusively to IP parse/range errors, not to port validity.
- IP range tab — counter label: when `error != null`, N is shown as 0.
- Duplicates: before building `Results`, hosts already present in the current computer collection (matched by `Host` value, case-insensitive) are silently removed from the result list.

## Integration

`MainViewModel` gets a new method to batch-add and save once:

```csharp
public void AddComputers(IEnumerable<ComputerConfig> configs)
{
    foreach (var config in configs)
        Computers.Add(new ComputerViewModel(config));
    SaveComputers();
}
```

`ComputersManagerWindow.AddMultiple_Click`:

```csharp
private void AddMultiple_Click(object sender, RoutedEventArgs e)
{
    var win = new AddMultipleComputersWindow(
        _mainVm.Computers.Select(c => c.Host).ToHashSet(StringComparer.OrdinalIgnoreCase))
    { Owner = this };
    if (win.ShowDialog() == true && win.Results.Count > 0)
        _mainVm.AddComputers(win.Results);
}
```

The existing `Host` values are passed in so the window can exclude duplicates without depending on `MainViewModel` directly.

## Files Affected

| File | Change |
|---|---|
| `Views/AddMultipleComputersWindow.xaml` | **New** |
| `Views/AddMultipleComputersWindow.xaml.cs` | **New** |
| `Services/BulkComputerParser.cs` | **New** — parsing + `GenerateApiKey()` |
| `Views/ComputersManagerWindow.xaml` | Add `BtnAddMultiple` button to toolbar |
| `Views/ComputersManagerWindow.xaml.cs` | Add `AddMultiple_Click` handler |
| `ViewModels/MainViewModel.cs` | Add `AddComputers(IEnumerable<ComputerConfig>)` |
| `ScreensView.Tests/BulkComputerParserTests.cs` | **New** — unit tests for parse logic |
| `Views/AddEditComputerWindow.xaml.cs` | Replace private `GenerateApiKey()` with call to `BulkComputerParser.GenerateApiKey()` |
