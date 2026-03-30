# Refresh Interval Setting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist the Viewer refresh interval in `viewer-settings.json` and restore it on startup.

**Architecture:** Reuse the existing `ViewerSettingsService` and extend `ViewerSettings` with a `RefreshIntervalSeconds` field. Keep `MainViewModel` responsible for normalizing the saved value, exposing it to the UI, persisting changes, and restarting the poller when needed.

**Tech Stack:** C#, WPF, CommunityToolkit.Mvvm, xUnit, System.Text.Json

---

### Task 1: Add red tests for refresh interval persistence

**Files:**
- Modify: `ScreensView.Tests/MainViewModelTests.cs`
- Test: `ScreensView.Tests/MainViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

Add tests that assert:
- constructor restores `RefreshInterval` from `ViewerSettings`
- changing `RefreshInterval` persists the new value through `IViewerSettingsService`

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter MainViewModelTests -v minimal`
Expected: FAIL because `ViewerSettings` and `MainViewModel` do not persist the refresh interval yet.

### Task 2: Implement minimal persistence

**Files:**
- Modify: `ScreensView.Viewer/Services/ViewerSettingsService.cs`
- Modify: `ScreensView.Viewer/ViewModels/MainViewModel.cs`

- [ ] **Step 1: Write the minimal implementation**

Implement:
- `ViewerSettings.RefreshIntervalSeconds`
- `MainViewModel` load-time normalization with default `5`
- save-on-change in `OnRefreshIntervalChanged`

- [ ] **Step 2: Run targeted tests**

Run: `dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter MainViewModelTests -v minimal`
Expected: PASS

### Task 3: Update docs and run full verification

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/specs/2026-03-30-refresh-interval-setting-design.md`
- Modify: `docs/superpowers/plans/2026-03-30-refresh-interval-setting.md`

- [ ] **Step 1: Document the saved setting**

Describe that `viewer-settings.json` now stores both autostart and refresh interval.

- [ ] **Step 2: Run the full test suite**

Run: `dotnet test`
Expected: PASS
