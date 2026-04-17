# Low/Info Audit Fixes

## Goal

Close audit items `L1-L9` from `docs/audit-2026-04-17.md` with low-risk hardening and cleanup across both agents and the Viewer.

## Completed Work

### Task 1: Screenshot-path hardening

- Added shared helpers:
  - `ScreensView.Shared/ScreenshotQuality.cs`
  - `ScreensView.Shared/SingleFlightGate.cs`
  - `ScreensView.Shared/ScreenshotBusyException.cs`
- Modern and legacy agents now clamp JPEG quality through shared code.
- Overlapping `/screenshot` requests now return `429 Too Many Requests`.
- Replaced `TOKEN_ALL_ACCESS` with the minimum primary-token access required by `CreateProcessAsUser`.

### Task 2: WMI and disposable cleanup

- Extracted the viewer service lookup WQL into `RemoteAgentInstaller.BuildServiceLookupQuery()`.
- Explicitly disposed `TcpListener` in `LlamaServerProcessService.FindFreePort()`.
- Explicitly disposed `ManagementObject` instances in `RemotePowerService`.

### Task 3: Viewer presentation and localization cleanup

- Moved converter types from `App.xaml.cs` into `ScreensView.Viewer/Converters.cs`.
- Replaced duplicated `120`-second timeout literals in `LlmCheckService` with named constants.
- Reworked localized status text in `MainViewModel` and `ComputerViewModel` so language switching refreshes text correctly.
- Added regression coverage for headless localization access and language-switch refresh.
- Disabled xUnit test parallelization to avoid cross-test races on static localization state.

### Task 4: Test and doc stabilization

- Updated documentation/layout tests to follow the current localized XAML structure instead of hard-coded inline Russian strings.
- Added button names in `ConnectionsFilePasswordWindow` so the layout test no longer depends on unresolved localized button content.
- Recorded closure notes in `docs/audit-2026-04-17.md`.

## Verification

- Targeted hardening tests passed.
- Targeted viewer/localization tests passed.
- Full suite passed with `dotnet test -v minimal`:
  - `363` passed
  - `0` failed
  - `0` skipped

## Commits in this worktree

- `fix: harden screenshot capture paths in both agents`
- `refactor: clean up WMI queries and disposable resource handling`
- Final cleanup and documentation are included in the closing commit for this worktree.
