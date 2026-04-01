# Refresh Now Toolbar Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a toolbar button that fetches fresh screenshots for all enabled computers immediately without changing auto-polling state or interval.

**Architecture:** Keep the button in the main toolbar and bind it to a new `MainViewModel` command. Extend `IScreenshotPollerService` with a one-shot refresh entry point and reuse the same polling pipeline so manual and automatic refreshes stay consistent.

**Tech Stack:** WPF, CommunityToolkit.Mvvm, xUnit, .NET 8

---

### Task 1: Cover the new toolbar action with tests

**Files:**
- Modify: `ScreensView.Tests/MainViewModelTests.cs`
- Modify: `ScreensView.Tests/WindowLayoutTests.cs`

- [ ] **Step 1: Write the failing tests**

Add a `MainViewModelTests` case that executes `RefreshNowCommand` and asserts:
- one manual refresh call is recorded
- no extra `Start` or `Stop` calls happen
- the current polling state stays unchanged

Add a `WindowLayoutTests` case that asserts `MainWindow.xaml` contains a toolbar button bound to `RefreshNowCommand`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter "MainViewModelTests|WindowLayoutTests"`
Expected: FAIL because the command and toolbar button do not exist yet.

### Task 2: Implement one-shot screenshot refresh

**Files:**
- Modify: `ScreensView.Viewer/Services/ScreenshotPollerService.cs`
- Modify: `ScreensView.Viewer/ViewModels/MainViewModel.cs`
- Modify: `ScreensView.Viewer/MainWindow.xaml`

- [ ] **Step 1: Add the minimal poller API**

Extend `IScreenshotPollerService` with `Task RefreshNowAsync(IEnumerable<ComputerViewModel> computers)` and implement it in `ScreenshotPollerService` by reusing the existing batch polling path for enabled computers only.

- [ ] **Step 2: Add the view-model command**

Expose `RefreshNowCommand` from `MainViewModel` and route it to the new poller API without changing `IsPolling` or restarting the interval timer.

- [ ] **Step 3: Add the toolbar button**

Place a new toolbar button in `MainWindow.xaml` near the existing polling controls and bind it to `RefreshNowCommand`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter "MainViewModelTests|WindowLayoutTests"`
Expected: PASS.

### Task 3: Verify, document, and commit

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Update user-facing docs**

Document that the main toolbar now has an `Обновить сейчас` action for immediate screenshot refresh.

- [ ] **Step 2: Run the full test suite**

Run: `dotnet test ScreensView.slnx`
Expected: PASS with zero failing tests.

- [ ] **Step 3: Commit**

Commit the code, tests, and docs with a focused message describing the new manual refresh action.
