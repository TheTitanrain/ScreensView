# ScreensView

Screen monitoring system for computers inside a local network. An agent on each machine exposes a screenshot over HTTPS, and the Viewer displays all screens in a single wall.

- Public showcase: <https://titanrain.github.io/ScreensView/>
- Russian guide: [README.md](README.md)

## Solution Structure

| Project | Purpose |
|---|---|
| `ScreensView.Agent` | Modern Windows Service (`.NET 8`) for Windows 10/11 and supported server OS versions |
| `ScreensView.Agent.Legacy` | Legacy Windows Service (`.NET Framework 4.8`) for Windows 7 SP1 |
| `ScreensView.Viewer` | WPF application for the screenshot wall and computer management |
| `ScreensView.Shared` | Shared models, constants, and the agent JSON contract |
| `ScreensView.Tests` | xUnit tests for shared contracts, Viewer services, LLM pipeline, and remote deployment workflows |

## Development

- The main repository branch is `master`.
- Short-lived feature branches are removed after merge into `master` so local worktrees do not accumulate merged branches.

### Build and Test

```bash
dotnet build
dotnet test ScreensView.Tests/ScreensView.Tests.csproj
```

- `dotnet build` compiles the whole solution, including Viewer, modern/legacy agents, and the shared library.
- `ScreensView.Tests` contains the main verification suite for shared contracts, Viewer services, the LLM pipeline, and remote agent deployment scenarios.

## Requirements

- **Viewer**: `.NET 8`, Windows 10/11
- **Modern Agent**: `.NET 8`, Windows 10/11
- **Legacy Agent**: Windows 7 SP1 with `.NET Framework 4.8`
- Agent installation requires administrative privileges because the service provisions a certificate and HTTPS binding
- **Both agents must run as `LocalSystem`**. This is required by `WTSQueryUserToken` for screen capture from a Windows Service. A service running under another account will fail to capture the screen because `WTSQueryUserToken` requires `SeTcbPrivilege`, which is only available to `LocalSystem`
- Remote installation requires local administrator rights on the target machine, an accessible `Admin$` share, and working WMI access (`135` + dynamic RPC)

> Windows 7 support is a compatibility path. In `2026`, it is not considered a Microsoft-supported modern platform: `.NET Framework 4.8` follows the lifecycle of the base Windows OS.

## Quick Start

### 1. Agent Configuration

Both agent variants use the same `appsettings.json`:

```json
{
  "Agent": {
    "Port": 5443,
    "ApiKey": "YOUR_SECRET_KEY",
    "ScreenshotQuality": 75
  }
}
```

### 2. Modern Agent (`Windows 10/11`)

**Run for testing** (as Administrator):

```bash
dotnet run --project ScreensView.Agent
```

**Install as a Windows Service** (as Administrator):

```text
sc create ScreensViewAgent binPath= "C:\path\to\ScreensView.Agent.exe" start= auto
sc start ScreensViewAgent
```

### 3. Legacy Agent (`Windows 7 SP1`)

**Build the legacy agent**:

```bash
dotnet build ScreensView.Agent.Legacy/ScreensView.Agent.Legacy.csproj
```

**Install as a Windows Service** (as Administrator):

```text
sc create ScreensViewAgent binPath= "C:\path\to\ScreensView.Agent.Legacy.exe" start= auto
sc start ScreensViewAgent
```

The legacy agent creates a self-signed certificate and refreshes the HTTPS binding through `netsh http` on startup.

### 4. Agent Health Check

```bash
curl -k -H "X-Api-Key: YOUR_SECRET_KEY" https://localhost:5443/health
```

> If no active console user exists or the workstation is locked, `/screenshot` returns **HTTP 503** instead of an image. This is expected behavior: Viewer marks the machine as `Locked` and shows a lock overlay because the agent cannot capture the secure desktop.

### 5. Viewer

```bash
dotnet run --project ScreensView.Viewer
```

1. Click **Computers** to open **Manage Computers**.
2. Use **Add** for one computer or **Add multiple** for bulk entry. The bulk dialog supports **By hosts** and **By IP range**, auto-generates API keys, and can immediately offer agent installation for the new machines.
3. In **Add computer**, fill in the **Computer**, **Connection**, and optionally **Screen description** sections for LLM comparison: name, host, port, and API key.
4. The **Manage Computers** window supports multi-select: **Delete**, **Install agent**, and **Remove agent** apply to the selected rows, while **Update agents** updates all enabled computers.
5. The **Enabled** column can be toggled directly in the table. Disabled machines are not polled by Viewer, do not participate in bulk agent updates, and display an explicit disabled mark on the card.
6. Click **Start** to begin polling screenshots.
7. Double-click a card or use **Open** from the context menu to open a dedicated zoom window with the live screenshot. Locked machines do not open in zoom; the card remains in the grid with the lock overlay.
8. Right-clicking a tile opens a context menu with **Open**, **Edit**, **Run LLM now**, **Ping**, and **Delete**.
9. If needed, open **Settings** and enable **Autostart** so Viewer launches with Windows.

### LLM Screen Analysis

Viewer can locally compare a screenshot with the expected screen description.

1. Open **Settings** and enable **Screen analysis**.
2. On first use, pick a model and click **Download**. The default is a compatible `LLaVA v1.5 7B`. Experimental entries such as `Gemma 4 E2B`, `Qwen3.5-0.8B`, and other GGUF variants can also appear in Settings.
3. If the selected backend is `CUDA (NVIDIA)`, Viewer downloads two separate archives from the `llama.cpp` release: the main `llama-...-bin-win-cuda-12...zip` containing `llama-server.exe` and a separate `cudart-llama-...-bin-win-cuda-12...zip` archive containing the CUDA runtime DLLs. If the backend status shows `Not downloaded` or `Partially downloaded`, use **Download backend** in Settings to repair the local binaries.
4. Open **Edit** for the computer and fill in **Screen description**.

- The screen description helps LLM compare the current screenshot with the expected screen type.
- Focus on stable visual structure: large blocks, columns, color zones, timelines, tables, or tiles.
- Do not list small, constantly changing details such as precise timestamps, identifiers, surnames, comments, or other dynamic fields.
- Example: `A scoreboard with large numbers on the left and wide rows on the right; the overall structure, color zones, and large time intervals matter more than exact values.`

5. After that, the card shows an LLM badge in the top-right corner:

- `?` - service enabled but screen description is missing
- `LLM` - description exists and the first check is pending
- `...` - analysis of the current screenshot is in progress
- `✓` - screen matches the description
- `✗` - screen does not match the description
- `!` - analysis error

If screen analysis is disabled, the badge is hidden on all cards.
The **LLM now** toolbar button runs an out-of-band LLM check for all cards immediately.
The context menu also exposes **Run LLM now** for a single computer.
Viewer marshals command availability updates back to the WPF dispatcher, so manual LLM execution remains safe even if model startup completes on a background thread after a download or model switch.

If the model or projector fails to load, Viewer shows `Model load error` in Settings and writes details to `%AppData%\ScreensView\logs\viewer.log`.
If the log contains `unknown model architecture`, the selected GGUF is not supported by the current `LLama` runtime even if the files downloaded successfully.
For multimodal requests, Viewer uses a fast profile: it resizes screenshots to a `768 px` long edge, encodes JPEG with quality `80`, limits the model response, and for some Qwen variants additionally lowers the image token budget on the `llama-server` side.
If a screenshot exactly matches one of the last 16 already analyzed frames for the same computer, Viewer reuses the cached result and skips another LLM call.
During screen analysis, `viewer.log` writes the outcome per computer and separate events such as `CacheHit`, `CacheMiss`, `SkipStaleScreenshot`, `Timeout`, and `Cancelled`.
For LLM debugging, `viewer.log` also writes `LlmInferenceService.RawResponseMismatch`, `LlmInferenceService.RawResponseEmptyExplanation`, and `LlmInferenceService.RawResponseParseError` with the raw model response and the normalized string after stripping `<think>...</think>`.

## Remote Agent Installation and Maintenance

The **Manage Computers** window provides:

- **Install agent** for one or more selected computers
- **Remove agent** for one or more selected computers
- **Update agents** for all enabled computers without requiring a manual row selection
- An optional immediate install offer after **Add multiple** for the freshly added set

Viewer automatically selects the payload for the target OS:

- `Windows 10/11` and supported server versions -> `ScreensView.Agent` (`.NET 8`)
- `Windows 7 SP1` + `.NET Framework 4.8` -> `ScreensView.Agent.Legacy` (`net48`)
- `Windows 7` without `.NET Framework 4.8` -> installation stops with a diagnostic error
- Unsupported OS versions -> installation stops with an explicit message

Target machine requirements:

- `Admin$` must be available (`\\hostname\Admin$`)
- WMI must be reachable (`135` + dynamic RPC)
- credentials must have local administrator rights

Viewer copies the selected payload into `C:\Windows\ScreensViewAgent\`, creates the `ScreensViewAgent` service, and starts it.
For install, uninstall, and update operations, Viewer prompts for local administrator credentials on the target machine and uses them for `Admin$`, WMI, and service control.

The install/update/uninstall progress window shows each step in real time with color coding: green for success, red for failure, yellow for warning (for example, the service could not stop but the operation continued).

## Security

| Threat | Protection |
|---|---|
| Traffic interception | HTTPS (TLS 1.2+), self-signed certificate |
| Unauthorized access to the agent | API key in the `X-Api-Key` header |
| Certificate substitution | Pinned SHA-256 thumbprint in Viewer |

The certificate is generated automatically on first agent start and stored in `LocalMachine\My`.

## Updates

### Agent (from Viewer)

Choose a computer -> **Manage Computers** -> Viewer stops the service, replaces the payload, and starts the service again. **Update agents** in the same window updates all active machines in parallel while preserving the modern/legacy selection for each OS.

### Viewer (auto-update)

On startup, Viewer checks GitHub Releases. If a new version exists, Viewer offers an update. The same check is available manually from the **About** window through **Check for updates**. The repository URL used by `ScreensView.Viewer/Services/ViewerUpdateService.cs` is:

```csharp
private const string GitHubReleasesUrl =
    "https://api.github.com/repos/titanrain/ScreensView/releases/latest";
```

### Viewer Settings

Viewer stores its local settings in `%AppData%\ScreensView\viewer-settings.json`.

- **Settings** is the single place for all mutable Viewer settings: `General`, `Screen analysis`, and `Connections storage`.
- The **Refresh interval** slider in `General` stores `RefreshIntervalSeconds` and restores it on the next launch.
- **Refresh now** in the toolbar requests fresh screenshots from all enabled computers without changing the background polling state.
- **LLM now** in the toolbar runs an out-of-band LLM check against the current screenshots for all machines that have screen analysis enabled and a ready model.
- **Autostart when I sign in to Windows** in `General` enables or disables Viewer startup at user logon.
- On Windows, this corresponds to the `ScreensView` value in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- Viewer also stores connections-source metadata: the path to the external file and the locally remembered password for that file if the user chose `Remember password on this computer`.
- The top toolbar contains only **Computers**, **Start/Stop**, **Refresh now**, **LLM now**, **Settings**, and **About**.

### Connections File

Viewer uses the local `%AppData%\ScreensView\computers.json` by default.

- In local mode, the computer list belongs only to the current Windows user.
- `ApiKey` fields in the local `computers.json` are stored through DPAPI.
- **Screen description** is saved together with the rest of the computer configuration and restored after restart.
- If `ConnectionsFilePath` in `%AppData%\ScreensView\viewer-settings.json` is empty, the local file remains the active source.

Viewer can switch to an external encrypted connections file through **Settings -> Connections storage -> Connections file...**.

- If you select a new file, Viewer asks for a password, creates the encrypted container, and migrates the current list into it.
- If you select an existing file, Viewer asks for the password to open it.
- The external file stores the full list in encrypted form (`PBKDF2-SHA256` + `AES-GCM`), including `ApiKey`.
- Before selecting an external file, Viewer warns that it should not be stored in a broadly accessible shared folder without access restrictions.

#### How to Create a Shared Connections File for the First Time

1. First, add all required computers in the normal local mode.
2. Open **Settings**.
3. In **Connections storage**, click **Connections file...**.
4. Pick a new file in a shared folder with restricted access.
5. Set a password for the new file and optionally enable **Remember password on this computer**.
6. Viewer immediately exports the current connections list into the encrypted file and switches to it.

After that:

- all further computer-list changes are saved into the shared file;
- other users can select the same file and open it with the password;
- each user can decide whether to remember the password locally on their own machine.

If the list is still empty, you can create the external file first and populate it afterward.

### External File Password

- The password is required to open the external connections file.
- **Remember password on this computer** stores the password only for the current Windows user on that computer and only in encrypted form.
- The remembered password is encrypted with DPAPI and stored in `%AppData%\ScreensView\viewer-settings.json`.
- If the remembered password stops working, Viewer clears it and prompts for manual entry again.
- If a wrong password is entered during startup before the main window exists, Viewer shows the warning and does not crash because of a missing owner window.
- During early startup before `MainWindow` is shown, Viewer keeps the process alive between those dialogs so the password can be retried immediately.
- The password window grows vertically with the file path and text size so the **Cancel** and **OK** buttons remain visible.
- On startup, Viewer does not silently fall back to the local file: the user must enter the password, explicitly switch back to local storage, or cancel startup.

### Revert to the Local File

In **Settings**, the **Connections storage** section exposes an explicit **Use local file** action.

- Viewer saves the current set of connections back to `%AppData%\ScreensView\computers.json`.
- `ConnectionsFilePath` and the locally remembered password are cleared from `viewer-settings.json`.
- The list stays available in the UI immediately after the switch, without restarting the application.
- The **Manage Computers** window preserves the read-only source indicator so the operator can see where the editable list is stored.

## Agent Configuration

| Parameter | Default | Description |
|---|---|---|
| `Agent:Port` | `5443` | HTTPS port |
| `Agent:ApiKey` | *(required)* | Secret authorization key |
| `Agent:ScreenshotQuality` | `75` | JPEG quality (1-100) |
