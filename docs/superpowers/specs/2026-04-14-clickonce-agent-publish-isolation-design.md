# Fix: Isolate Agent Payload Builds from Viewer ClickOnce Publish

**Date:** 2026-04-14
**Status:** Approved

## Problem

Publishing `ScreensView.Viewer` with the ClickOnce profile fails during the nested build of `ScreensView.Agent.Legacy`.

The Visual Studio publish log shows that the legacy agent is being compiled into `obj\Release\net8.0-windows\...` instead of `net48`, which then breaks the `System.ServiceProcess.ServiceBase` reference and aborts publish.

At the same time, the nested publish of `ScreensView.Agent` inherits Viewer ClickOnce properties and generates its own `setup.exe` and `.application` artifacts inside the agent publish output. These files are unrelated to the payload the Viewer needs to embed.

## Root Cause

`ScreensView.Viewer.csproj` builds agent payloads through the custom target `BuildAgentPayloads`:

- `ScreensView.Agent` is invoked with `Restore;Publish`
- `ScreensView.Agent.Legacy` is invoked with `Restore;Build`

The target already removes `TargetFramework` and `PublishProfile`, but that isolation is incomplete for ClickOnce publish.

During Viewer publish, additional global properties from the outer ClickOnce context still affect the nested agent invocations:

1. The legacy agent can be evaluated under the outer `net8.0-windows` target context instead of its own `net48`.
2. The modern agent publish inherits ClickOnce-oriented properties and emits deployment artifacts that do not belong in the embedded payload.

This is a publish-context leak, not a problem in the legacy agent code itself.

## Solution

Tighten isolation in `ScreensView.Viewer.csproj` so nested agent builds always run in their own intended context.

### `BuildAgentPayloads`

For the nested `MSBuild` calls:

- Explicitly set `TargetFramework=net8.0-windows` for `ScreensView.Agent`
- Explicitly set `TargetFramework=net48` for `ScreensView.Agent.Legacy`
- Remove additional outer publish properties that should never flow into nested payload builds, including ClickOnce-specific publish globals

The implementation should prefer isolating the nested invocations over changing the agent projects themselves.

## Scope

Files expected to change:

| File | Change |
|------|--------|
| `ScreensView.Viewer/ScreensView.Viewer.csproj` | Harden nested MSBuild isolation for agent payload builds during Viewer publish |
| `README.md` and/or `README.en.md` | Only if a short note is needed to document the publish constraint or workflow |

Files explicitly out of scope:

- `ScreensView.Agent.Legacy` service implementation
- ClickOnce profile functional behavior for the Viewer itself
- Viewer runtime behavior

## Expected Result

- Viewer ClickOnce publish succeeds from Visual Studio.
- `ScreensView.Agent.Legacy` is always built as `net48` during nested payload staging.
- `ScreensView.Agent` payload publish no longer carries unintended Viewer ClickOnce deployment artifacts into the staged payload.
- Existing payload copy and validation targets continue to work unchanged.

## Verification

Minimum verification after implementation:

1. Publish `ScreensView.Viewer` with `ClickOnceProfile` from Visual Studio or `MSBuild.exe`.
2. Confirm the publish completes without the `ServiceBase` / `System.ServiceProcess` failure.
3. Confirm `ScreensView.Viewer` still produces its own ClickOnce artifacts in `app.publish`.
4. Confirm staged agent payload folders still contain the expected executables.
