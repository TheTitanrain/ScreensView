# Main Window Visual Refresh Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refresh the main `ScreensView.Viewer` window so it looks like a modern light dashboard while preserving the existing monitoring workflows and behavior.

**Architecture:** Keep all business behavior intact and treat this as a presentation-focused change. Add reusable visual resources in `App.xaml`, expose only minimal summary/presentation properties from the view-model layer, and rebuild `MainWindow.xaml` around a custom header, compact summary cards, and updated computer tiles.

**Tech Stack:** WPF / net8.0-windows, XAML styles/resources, CommunityToolkit.Mvvm, xUnit

---

## File Map

| File | Action | Responsibility |
|------|--------|----------------|
| `ScreensView.Viewer/App.xaml` | Modify | Shared palette, radii, brushes, button/toggle/checkbox/slider/card styles |
| `ScreensView.Viewer/MainWindow.xaml` | Modify | New visual structure for header, summary row, and computer cards |
| `ScreensView.Viewer/ViewModels/MainViewModel.cs` | Modify | Summary properties for counts and polling state text |
| `ScreensView.Viewer/ViewModels/ComputerViewModel.cs` | Modify | Optional presentation helpers such as status text / screenshot presence |
| `ScreensView.Tests/MainViewModelTests.cs` | Modify | Tests for summary properties and refresh-related presentation output |
| `ScreensView.Tests/ComputerViewModelTests.cs` | Modify | Tests for any new presentation helpers on card state |
| `README.md` | Modify | Short note that Viewer main window uses a refreshed dashboard layout if the implementation changes user-facing guidance |

---

### Task 1: Add red tests for new presentation state

**Files:**
- Modify: `ScreensView.Tests/MainViewModelTests.cs`
- Modify: `ScreensView.Tests/ComputerViewModelTests.cs`
- Test: `ScreensView.Tests/MainViewModelTests.cs`
- Test: `ScreensView.Tests/ComputerViewModelTests.cs`

- [x] **Step 1: Add failing `MainViewModel` tests for summary counts**

Add tests that construct a `MainViewModel` with a mix of `Online`, `Offline`, `Error`, `Locked`, and disabled computers, then assert:

- active computer count excludes disabled entries
- online count includes only `ComputerStatus.Online`
- problem count includes `Offline` and `Error`
- polling summary text changes between stopped and running states

Example assertion target:

```csharp
Assert.Equal(3, vm.ActiveComputerCount);
Assert.Equal(1, vm.OnlineComputerCount);
Assert.Equal(2, vm.ProblemComputerCount);
Assert.Equal("Опрос остановлен", vm.PollingStateText);
```

- [x] **Step 2: Add failing `ComputerViewModel` tests for presentation helpers**

If the implementation will add helper properties, add tests first. Keep them narrow:

- `StatusText` maps each enum value to the expected Russian label
- `HasScreenshot` switches from `false` to `true` after `UpdateScreenshot`

Example assertion target:

```csharp
Assert.Equal("Не в сети", vm.StatusText);
Assert.False(vm.HasScreenshot);
```

- [x] **Step 3: Run targeted tests to confirm failures**

Run:

```bash
dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter "MainViewModelTests|ComputerViewModelTests" -v minimal
```

Expected: FAIL because the new summary/presentation properties do not exist yet.

- [x] **Step 4: Commit**

```bash
git add ScreensView.Tests/MainViewModelTests.cs ScreensView.Tests/ComputerViewModelTests.cs
git commit -m "test: cover main window visual refresh presentation state"
```

---

### Task 2: Implement summary and card presentation properties

**Files:**
- Modify: `ScreensView.Viewer/ViewModels/MainViewModel.cs`
- Modify: `ScreensView.Viewer/ViewModels/ComputerViewModel.cs`
- Test: `ScreensView.Tests/MainViewModelTests.cs`
- Test: `ScreensView.Tests/ComputerViewModelTests.cs`

- [x] **Step 1: Add minimal summary properties to `MainViewModel`**

Implement read-only properties backed by the existing `Computers` collection:

- `ActiveComputerCount`
- `OnlineComputerCount`
- `ProblemComputerCount`
- `PollingStateText`
- optional `RefreshIntervalText` if it noticeably simplifies XAML

Keep the logic presentation-only; do not add new services or persistence.

- [x] **Step 2: Raise property change notifications when card state changes**

Ensure summary properties refresh when:

- computers are added or removed
- `Status`, `IsEnabled`, or similar card-relevant values change
- polling starts/stops
- refresh interval changes if used in summary text

The simplest acceptable implementation is subscribing to item `PropertyChanged` and calling `OnPropertyChanged(...)` for the derived summary properties.

- [x] **Step 3: Add minimal helpers to `ComputerViewModel` only if they reduce XAML complexity**

If needed, implement focused presentation helpers such as:

- `bool HasScreenshot => Screenshot is not null;`
- `string StatusText => ...`

Do not move UI styling logic into the view model.

- [x] **Step 4: Run targeted tests**

Run:

```bash
dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter "MainViewModelTests|ComputerViewModelTests" -v minimal
```

Expected: PASS

- [x] **Step 5: Commit**

```bash
git add ScreensView.Viewer/ViewModels/MainViewModel.cs ScreensView.Viewer/ViewModels/ComputerViewModel.cs ScreensView.Tests/MainViewModelTests.cs ScreensView.Tests/ComputerViewModelTests.cs
git commit -m "feat: add main window summary presentation state"
```

---

### Task 3: Build shared visual resources in `App.xaml`

**Files:**
- Modify: `ScreensView.Viewer/App.xaml`

- [x] **Step 1: Define the shared palette and surface tokens**

Add application-level resources for:

- background, surface, accent, border, success, warning, error brushes
- corner radii / spacing values where useful
- shared typography colors for primary/secondary/caption text

Keep the names explicit, for example:

```xml
<SolidColorBrush x:Key="WindowBackgroundBrush" Color="#F3F7FB"/>
<SolidColorBrush x:Key="SurfaceBrush" Color="White"/>
<SolidColorBrush x:Key="AccentBrush" Color="#0F766E"/>
```

- [x] **Step 2: Add reusable control styles**

Create shared styles for:

- standard action button
- primary/accent button
- polling toggle button
- checkbox
- slider
- summary tile border
- computer card border / footer text where practical

Do not try to style every control in the app; keep the scope tied to the main window.

- [x] **Step 3: Build viewer project**

Run:

```bash
dotnet build ScreensView.Viewer/ScreensView.Viewer.csproj
```

Expected: `Build succeeded.`

- [x] **Step 4: Commit**

```bash
git add ScreensView.Viewer/App.xaml
git commit -m "feat: add shared viewer theme resources"
```

---

### Task 4: Rebuild `MainWindow.xaml` on the new style system

**Files:**
- Modify: `ScreensView.Viewer/MainWindow.xaml`
- Modify: `ScreensView.Viewer/MainWindow.xaml.cs`

- [x] **Step 1: Replace the stock toolbar with a custom command bar**

Rework the top of `MainWindow.xaml` so it uses layout containers (`Grid`, `Border`, `StackPanel`, etc.) instead of the default `ToolBar` chrome.

Preserve the existing commands and bindings:

- `ManageComputers_Click`
- `RefreshInterval`
- `TogglePollingCommand`
- `UpdateAllAgents_Click`
- `IsAutostartEnabled`
- `About_Click`

- [x] **Step 2: Add a compact summary row under the header**

Render 3–4 small summary tiles using the new `MainViewModel` properties:

- active computers
- online computers
- problem computers
- polling state / interval

This row must remain informational only; no new commands or filters.

- [x] **Step 3: Refresh the computer card template**

Update the `ItemsControl.ItemTemplate` to match the approved visual direction:

- light card shell with more generous corner radius
- improved screenshot area and placeholder
- cleaner lock overlay
- refined footer with status dot, computer name, status text if used, and last update time

Keep double-click behavior unchanged by preserving `MouseLeftButtonDown="Card_MouseLeftButtonDown"`.

- [x] **Step 4: Keep code-behind minimal**

Only touch `MainWindow.xaml.cs` if needed for layout-related support that cannot be expressed in bindings. Do not move behavior out of the existing handlers unless required for compile correctness.

- [x] **Step 5: Build viewer project**

Run:

```bash
dotnet build ScreensView.Viewer/ScreensView.Viewer.csproj
```

Expected: `Build succeeded.`

- [x] **Step 6: Manual smoke check**

Run:

```bash
dotnet run --project ScreensView.Viewer/ScreensView.Viewer.csproj
```

Verify:

- header renders with the new command bar layout
- summary row shows values and updates when polling starts/stops
- cards still open zoom on double-click unless status is `Locked`
- placeholder and locked overlay render correctly

- [x] **Step 7: Commit**

```bash
git add ScreensView.Viewer/MainWindow.xaml ScreensView.Viewer/MainWindow.xaml.cs
git commit -m "feat: refresh main viewer window layout"
```

---

### Task 5: Update docs and run full verification

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/specs/2026-03-31-main-window-visual-refresh-design.md`
- Modify: `docs/superpowers/plans/2026-03-31-main-window-visual-refresh.md`

- [x] **Step 1: Update README only if user-visible guidance changed**

If the refreshed main window changes the way the toolbar/controls are described, adjust the short Viewer usage section accordingly. Keep the update concise.

- [x] **Step 2: Re-read the spec and plan for accuracy**

Confirm the implementation still matches the approved spec and update the documents only if reality diverged in a meaningful way.

- [x] **Step 3: Run the full test suite**

Run:

```bash
dotnet test
```

Expected: PASS

- [x] **Step 4: Commit**

```bash
git add README.md docs/superpowers/specs/2026-03-31-main-window-visual-refresh-design.md docs/superpowers/plans/2026-03-31-main-window-visual-refresh.md
git commit -m "docs: finalize main window visual refresh notes"
```

---

## Verification Checklist

- [x] `dotnet build ScreensView.Viewer/ScreensView.Viewer.csproj` succeeds
- [x] `dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter "MainViewModelTests|ComputerViewModelTests" -v minimal` succeeds
- [x] `dotnet test` succeeds
- [x] Main window keeps the existing workflows unchanged
- [x] New header, summary row, and card styling match the approved `A + 2` direction
