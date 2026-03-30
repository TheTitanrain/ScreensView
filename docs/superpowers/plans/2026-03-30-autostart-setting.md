# Autostart Setting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a toolbar setting that enables or disables Viewer autostart and persists the preference.

**Architecture:** Keep `MainViewModel` responsible for orchestration only. Introduce `ViewerSettingsService` for JSON persistence and `AutostartService` for Windows Registry access, then bind a toolbar `CheckBox` to the new view-model state.

**Tech Stack:** C#, WPF, CommunityToolkit.Mvvm, xUnit, Windows Registry

---

### Task 1: Add red tests for autostart state orchestration

**Files:**
- Modify: `ScreensView.Tests/MainViewModelTests.cs`
- Test: `ScreensView.Tests/MainViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

Add tests that assert:
- `MainViewModel` loads autostart state from services on construction
- enabling autostart updates the autostart backend and persists settings
- backend failure rolls the checkbox state back and does not persist the failed value

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter MainViewModelTests -v minimal`
Expected: FAIL because `MainViewModel` does not expose autostart state or depend on settings/autostart services yet.

- [ ] **Step 3: Commit**

```bash
git add ScreensView.Tests/MainViewModelTests.cs
git commit -m "test: cover viewer autostart setting"
```

### Task 2: Implement viewer settings and autostart orchestration

**Files:**
- Create: `ScreensView.Viewer/Services/ViewerSettingsService.cs`
- Create: `ScreensView.Viewer/Services/AutostartService.cs`
- Modify: `ScreensView.Viewer/ViewModels/MainViewModel.cs`

- [ ] **Step 1: Write the minimal implementation**

Implement:
- `ViewerSettings` model with `LaunchAtStartup`
- `ViewerSettingsService.Load()` / `Save()`
- `AutostartService.IsEnabled()` / `SetEnabled(bool)`
- `MainViewModel.IsAutostartEnabled` and rollback-safe update flow

- [ ] **Step 2: Run targeted tests**

Run: `dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter MainViewModelTests -v minimal`
Expected: PASS

- [ ] **Step 3: Refactor if needed**

Keep `MainViewModel` UI-agnostic except for a simple injected error callback.

- [ ] **Step 4: Commit**

```bash
git add ScreensView.Viewer/Services/ViewerSettingsService.cs ScreensView.Viewer/Services/AutostartService.cs ScreensView.Viewer/ViewModels/MainViewModel.cs ScreensView.Tests/MainViewModelTests.cs
git commit -m "feat: add viewer autostart setting services"
```

### Task 3: Wire the setting into the main window

**Files:**
- Modify: `ScreensView.Viewer/MainWindow.xaml`
- Modify: `ScreensView.Viewer/MainWindow.xaml.cs`

- [ ] **Step 1: Add the toolbar checkbox**

Add a `CheckBox` bound to `IsAutostartEnabled`.

- [ ] **Step 2: Wire error reporting**

Pass a callback from `MainWindow` to `MainViewModel` that shows a `MessageBox` when autostart update fails.

- [ ] **Step 3: Run targeted tests/build verification**

Run: `dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter MainViewModelTests -v minimal`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add ScreensView.Viewer/MainWindow.xaml ScreensView.Viewer/MainWindow.xaml.cs
git commit -m "feat: expose autostart toggle in toolbar"
```

### Task 4: Update documentation and run full verification

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/specs/2026-03-30-autostart-setting-design.md`
- Modify: `docs/superpowers/plans/2026-03-30-autostart-setting.md`

- [ ] **Step 1: Document the feature**

Describe where the autostart toggle lives and how it works on Windows.

- [ ] **Step 2: Run the full test suite**

Run: `dotnet test`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add README.md docs/superpowers/specs/2026-03-30-autostart-setting-design.md docs/superpowers/plans/2026-03-30-autostart-setting.md
git commit -m "docs: document viewer autostart setting"
```
