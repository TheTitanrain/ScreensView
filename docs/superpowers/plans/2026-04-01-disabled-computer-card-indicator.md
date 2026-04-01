# Disabled Computer Card Indicator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show a clear disabled-state indicator on Viewer computer cards when a computer is turned off in Computers Manager.

**Architecture:** Add a dedicated `ComputerStatus.Disabled` presentation state and keep it synchronized with `IsEnabled` inside `ComputerViewModel` and `MainViewModel.UpdateComputer`. Reuse that single state in both `MainWindow.xaml` and `ScreenshotZoomWindow.xaml` so the UI does not split logic between `Status` and `IsEnabled`.

**Tech Stack:** C#, WPF, CommunityToolkit.Mvvm, xUnit

---

### Task 1: Lock the behavior with tests

**Files:**
- Modify: `ScreensView.Tests/ComputerViewModelTests.cs`
- Modify: `ScreensView.Tests/MainViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

Add tests that prove:
- `ComputerViewModel` created from disabled config starts in `Disabled`
- changing `IsEnabled` to `false` moves the card to `Disabled`
- changing it back to `true` resets the card to `Unknown`
- `MainViewModel.UpdateComputer(...)` applies those transitions

- [ ] **Step 2: Run the targeted tests to verify they fail**

Run: `dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter "FullyQualifiedName~ComputerViewModelTests|FullyQualifiedName~MainViewModelTests"`

Expected: FAIL because `Disabled` behavior does not exist yet.

- [ ] **Step 3: Write the minimal implementation**

Implement only the code required to synchronize `ComputerStatus` with `IsEnabled`.

- [ ] **Step 4: Run the targeted tests to verify they pass**

Run: `dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter "FullyQualifiedName~ComputerViewModelTests|FullyQualifiedName~MainViewModelTests"`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add ScreensView.Tests/ComputerViewModelTests.cs ScreensView.Tests/MainViewModelTests.cs ScreensView.Viewer/ViewModels/ComputerViewModel.cs ScreensView.Viewer/ViewModels/MainViewModel.cs
git commit -m "fix: track disabled computers in card state"
```

### Task 2: Surface the state in the UI

**Files:**
- Modify: `ScreensView.Viewer/MainWindow.xaml`
- Modify: `ScreensView.Viewer/Views/ScreenshotZoomWindow.xaml`

- [ ] **Step 1: Extend the existing XAML triggers**

Add `Disabled` color mapping and tooltip text usage through the existing `Status`-based bindings.

- [ ] **Step 2: Run build/tests to verify no XAML regressions**

Run: `dotnet test ScreensView.slnx`

Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add ScreensView.Viewer/MainWindow.xaml ScreensView.Viewer/Views/ScreenshotZoomWindow.xaml
git commit -m "fix: show disabled state in viewer cards"
```

### Task 3: Update docs and finalize

**Files:**
- Modify: `README.md`
- Add: `docs/superpowers/specs/2026-04-01-disabled-computer-card-indicator-design.md`
- Add: `docs/superpowers/plans/2026-04-01-disabled-computer-card-indicator.md`

- [ ] **Step 1: Document the new disabled indicator**

Describe that disabling a computer in Computers Manager leaves it in the list but marks the card as disabled and stops polling it.

- [ ] **Step 2: Run the full verification**

Run: `dotnet test ScreensView.slnx`

Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add README.md docs/superpowers/specs/2026-04-01-disabled-computer-card-indicator-design.md docs/superpowers/plans/2026-04-01-disabled-computer-card-indicator.md
git commit -m "docs: describe disabled computer card indicator"
```
