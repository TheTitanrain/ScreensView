# Encrypted External Connections File Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let Viewer switch from the default local `%AppData%\ScreensView\computers.json` to a password-encrypted shared connections file, remember that password locally per Windows user, and allow an explicit switch back to the local file.

**Architecture:** Keep `viewer-settings.json` as the local source of viewer-only preferences plus metadata for the active connections source. Preserve the existing DPAPI-backed local `ComputerStorageService`, add a separate `EncryptedComputerStorageService` for password-encrypted shared files, and introduce a small coordinator that resolves startup state and drives source switching without pushing crypto logic into `MainViewModel`.

**Tech Stack:** C#, WPF, CommunityToolkit.Mvvm, xUnit, System.Text.Json, System.Security.Cryptography, DPAPI

---

## File Map

- `ScreensView.Viewer/Services/ViewerSettingsService.cs`
  - Persist `ConnectionsFilePath` and `ConnectionsFilePasswordEncrypted` alongside existing viewer settings.
- `ScreensView.Viewer/Services/IComputerStorageService.cs`
  - Define the shared `Load()` / `Save()` contract for local and encrypted storage implementations.
- `ScreensView.Viewer/Services/ComputerStorageService.cs`
  - Implement `IComputerStorageService` and keep the existing local DPAPI behavior unchanged.
- `ScreensView.Viewer/Services/EncryptedComputerStorageService.cs`
  - Create and read the password-encrypted external file container using `PBKDF2-SHA256` + `AES-GCM`.
- `ScreensView.Viewer/Services/ConnectionsStorageController.cs`
  - Resolve the active source at startup, switch between local and external files, and update saved settings/password metadata atomically.
- `ScreensView.Viewer/ViewModels/MainViewModel.cs`
  - Accept the storage abstraction, reload the `Computers` collection when the source changes, and restart polling safely when needed.
- `ScreensView.Viewer/App.xaml`
  - Remove `StartupUri` so startup can resolve the active storage before creating the main window.
- `ScreensView.Viewer/App.xaml.cs`
  - Build the startup flow that loads viewer settings, attempts to open the active connections source, and prompts for a password when necessary.
- `ScreensView.Viewer/MainWindow.xaml.cs`
  - Accept injected dependencies or a ready-made `MainViewModel` instead of constructing all services internally.
- `ScreensView.Viewer/Views/ComputersManagerWindow.xaml`
  - Add UI for the active source label, switching to an external file, and returning to the local file.
- `ScreensView.Viewer/Views/ComputersManagerWindow.xaml.cs`
  - Wire file dialogs, security prompts, and calls into the storage controller.
- `ScreensView.Viewer/Views/ConnectionsFilePasswordWindow.xaml`
  - Provide password entry, password confirmation for new files, and a “remember on this computer” option.
- `ScreensView.Viewer/Views/ConnectionsFilePasswordWindow.xaml.cs`
  - Validate the dialog state and expose password/remember choices to the calling UI flow.
- `ScreensView.Tests/ViewerSettingsServiceTests.cs`
  - Cover persistence of the external source path and locally remembered password metadata.
- `ScreensView.Tests/EncryptedComputerStorageServiceTests.cs`
  - Cover correct encryption/decryption, wrong-password failures, and on-disk container shape.
- `ScreensView.Tests/ConnectionsStorageControllerTests.cs`
  - Cover startup resolution, source switching, password lifecycle, and failure rollback.
- `ScreensView.Tests/MainViewModelTests.cs`
  - Cover source switching behavior inside the view model, including polling-safe collection replacement.
- `README.md`
  - Document local storage, encrypted external files, password prompts, local password caching, and the explicit return-to-local action.

**Implementation workflow:** Apply @superpowers/test-driven-development to every behavior change and @superpowers/verification-before-completion before the final completion claim or final commit.

---

### Task 1: Add red tests for external source metadata in viewer settings

**Files:**
- Create: `ScreensView.Tests/ViewerSettingsServiceTests.cs`
- Modify: `ScreensView.Viewer/Services/ViewerSettingsService.cs`
- Test: `ScreensView.Tests/ViewerSettingsServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

Add tests that assert:
- `ViewerSettingsService.Save()` persists `ConnectionsFilePath`
- `ViewerSettingsService.Save()` persists `ConnectionsFilePasswordEncrypted`
- loading a settings file that omits the new fields still returns valid defaults
- clearing the external source fields round-trips correctly

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter ViewerSettingsServiceTests -v minimal`
Expected: FAIL because `ViewerSettings` does not yet expose the new external-source fields.

- [ ] **Step 3: Commit**

```bash
git add ScreensView.Tests/ViewerSettingsServiceTests.cs
git commit -m "test: cover viewer external source settings"
```

### Task 2: Implement viewer settings support for external source metadata

**Files:**
- Modify: `ScreensView.Viewer/Services/ViewerSettingsService.cs`
- Test: `ScreensView.Tests/ViewerSettingsServiceTests.cs`

- [ ] **Step 1: Write the minimal implementation**

Implement:

```csharp
public class ViewerSettings
{
    public bool LaunchAtStartup { get; set; }
    public int RefreshIntervalSeconds { get; set; } = 5;
    public string ConnectionsFilePath { get; set; } = string.Empty;
    public string ConnectionsFilePasswordEncrypted { get; set; } = string.Empty;
}
```

Keep `Load()` backward compatible with older JSON that omits the new fields.

- [ ] **Step 2: Run targeted tests**

Run: `dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter ViewerSettingsServiceTests -v minimal`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add ScreensView.Viewer/Services/ViewerSettingsService.cs ScreensView.Tests/ViewerSettingsServiceTests.cs
git commit -m "feat: persist viewer external source metadata"
```

### Task 3: Add red tests for encrypted external storage

**Files:**
- Create: `ScreensView.Tests/EncryptedComputerStorageServiceTests.cs`
- Create: `ScreensView.Viewer/Services/IComputerStorageService.cs`
- Create: `ScreensView.Viewer/Services/EncryptedComputerStorageService.cs`
- Modify: `ScreensView.Viewer/Services/ComputerStorageService.cs`
- Test: `ScreensView.Tests/EncryptedComputerStorageServiceTests.cs`
- Test: `ScreensView.Tests/ComputerStorageServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

Add tests that assert:
- a saved external file round-trips the full `ComputerConfig` list with the correct password
- the raw file does not contain plaintext host names or API keys
- loading with the wrong password throws a predictable domain exception
- the external file container includes versioned metadata fields
- the existing local `ComputerStorageService` tests still describe the unchanged DPAPI behavior

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter EncryptedComputerStorageServiceTests -v minimal`
Expected: FAIL because the encrypted storage implementation does not exist yet.

- [ ] **Step 3: Commit**

```bash
git add ScreensView.Tests/EncryptedComputerStorageServiceTests.cs
git commit -m "test: cover encrypted connections storage"
```

### Task 4: Implement the storage abstraction and encrypted external file service

**Files:**
- Create: `ScreensView.Viewer/Services/IComputerStorageService.cs`
- Create: `ScreensView.Viewer/Services/EncryptedComputerStorageService.cs`
- Modify: `ScreensView.Viewer/Services/ComputerStorageService.cs`
- Test: `ScreensView.Tests/EncryptedComputerStorageServiceTests.cs`
- Test: `ScreensView.Tests/ComputerStorageServiceTests.cs`

- [ ] **Step 1: Write the minimal implementation**

Implement the storage contract:

```csharp
public interface IComputerStorageService
{
    List<ComputerConfig> Load();
    void Save(IEnumerable<ComputerConfig> computers);
}
```

Implement an encrypted container shape similar to:

```csharp
internal sealed class EncryptedConnectionsFile
{
    public int Version { get; set; } = 1;
    public string KdfSalt { get; set; } = string.Empty;
    public string Nonce { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string Ciphertext { get; set; } = string.Empty;
}
```

Use `PBKDF2-SHA256` to derive a key from the password and `AES-GCM` to encrypt the serialized list of `ComputerConfig`.

- [ ] **Step 2: Run targeted tests**

Run: `dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter EncryptedComputerStorageServiceTests -v minimal`
Expected: PASS

- [ ] **Step 3: Run regression tests for local storage**

Run: `dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter ComputerStorageServiceTests -v minimal`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add ScreensView.Viewer/Services/IComputerStorageService.cs ScreensView.Viewer/Services/EncryptedComputerStorageService.cs ScreensView.Viewer/Services/ComputerStorageService.cs ScreensView.Tests/EncryptedComputerStorageServiceTests.cs ScreensView.Tests/ComputerStorageServiceTests.cs
git commit -m "feat: add encrypted external connections storage"
```

### Task 5: Add red tests for startup resolution and source switching orchestration

**Files:**
- Create: `ScreensView.Tests/ConnectionsStorageControllerTests.cs`
- Create: `ScreensView.Viewer/Services/ConnectionsStorageController.cs`
- Test: `ScreensView.Tests/ConnectionsStorageControllerTests.cs`

- [ ] **Step 1: Write the failing tests**

Add tests that assert:
- startup with an empty `ConnectionsFilePath` resolves to the local storage source
- startup with a remembered password opens the encrypted external file without prompting again
- startup with a bad remembered password clears it and reports that manual password input is required
- switching to a new external file exports the current connections before persisting the path
- switching back to local storage clears both the external path and remembered password
- failed switches leave both viewer settings and the active source unchanged

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter ConnectionsStorageControllerTests -v minimal`
Expected: FAIL because the coordinator for startup and source switching does not exist yet.

- [ ] **Step 3: Commit**

```bash
git add ScreensView.Tests/ConnectionsStorageControllerTests.cs
git commit -m "test: cover connections source orchestration"
```

### Task 6: Implement the storage controller and atomic settings updates

**Files:**
- Create: `ScreensView.Viewer/Services/ConnectionsStorageController.cs`
- Modify: `ScreensView.Viewer/Services/ViewerSettingsService.cs`
- Modify: `ScreensView.Viewer/Services/ComputerStorageService.cs`
- Modify: `ScreensView.Viewer/Services/EncryptedComputerStorageService.cs`
- Test: `ScreensView.Tests/ConnectionsStorageControllerTests.cs`

- [ ] **Step 1: Write the minimal implementation**

Use small result objects instead of exceptions for normal flow, for example:

```csharp
internal sealed record ResolveConnectionsSourceResult(
    IComputerStorageService Storage,
    IReadOnlyList<ComputerConfig> Computers,
    bool UsesExternalFile,
    bool NeedsPassword);
```

The controller should:
- resolve the startup source from `ViewerSettings`
- accept an optional remembered password from DPAPI-backed settings
- clear `ConnectionsFilePasswordEncrypted` when the remembered password is invalid
- persist `ConnectionsFilePath` and `ConnectionsFilePasswordEncrypted` only after a switch fully succeeds

- [ ] **Step 2: Run targeted tests**

Run: `dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter ConnectionsStorageControllerTests -v minimal`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add ScreensView.Viewer/Services/ConnectionsStorageController.cs ScreensView.Viewer/Services/ViewerSettingsService.cs ScreensView.Viewer/Services/ComputerStorageService.cs ScreensView.Viewer/Services/EncryptedComputerStorageService.cs ScreensView.Tests/ConnectionsStorageControllerTests.cs
git commit -m "feat: add connections source controller"
```

### Task 7: Add red tests for `MainViewModel` source replacement

**Files:**
- Modify: `ScreensView.Tests/MainViewModelTests.cs`
- Modify: `ScreensView.Viewer/ViewModels/MainViewModel.cs`
- Test: `ScreensView.Tests/MainViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

Add tests that assert:
- `MainViewModel` can replace the active storage source and swap the `Computers` collection contents
- switching sources while polling is active restarts the poller against the new set of computers
- a failed switch attempt leaves the current collection unchanged

If current concrete dependencies make this untestable, add interface seams first in the test design:

```csharp
public interface IScreenshotPollerService
{
    void Start(IEnumerable<ComputerViewModel> computers, int intervalSeconds);
    void Stop();
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter MainViewModelTests -v minimal`
Expected: FAIL because `MainViewModel` cannot swap storage sources or restart polling around that operation yet.

- [ ] **Step 3: Commit**

```bash
git add ScreensView.Tests/MainViewModelTests.cs
git commit -m "test: cover view model connections source switching"
```

### Task 8: Implement source replacement in `MainViewModel`

**Files:**
- Modify: `ScreensView.Viewer/ViewModels/MainViewModel.cs`
- Modify: `ScreensView.Viewer/Services/IComputerStorageService.cs`
- Modify: `ScreensView.Viewer/Services/ComputerStorageService.cs`
- Modify: `ScreensView.Viewer/Services/ScreenshotPollerService.cs`
- Test: `ScreensView.Tests/MainViewModelTests.cs`

- [ ] **Step 1: Write the minimal implementation**

Refactor `MainViewModel` to depend on abstractions instead of hard-coded local storage:
- accept `IComputerStorageService`
- accept an interface or adapter for the poller if needed for testing
- add a method that replaces the active storage and rehydrates `Computers`
- preserve the current collection if the controller reports a failed switch

- [ ] **Step 2: Run targeted tests**

Run: `dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter MainViewModelTests -v minimal`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add ScreensView.Viewer/ViewModels/MainViewModel.cs ScreensView.Viewer/Services/IComputerStorageService.cs ScreensView.Viewer/Services/ComputerStorageService.cs ScreensView.Viewer/Services/ScreenshotPollerService.cs ScreensView.Tests/MainViewModelTests.cs
git commit -m "feat: support runtime connections source switching"
```

### Task 9: Wire startup and UI flows for external/local source selection

**Files:**
- Modify: `ScreensView.Viewer/App.xaml`
- Modify: `ScreensView.Viewer/App.xaml.cs`
- Modify: `ScreensView.Viewer/MainWindow.xaml.cs`
- Modify: `ScreensView.Viewer/Views/ComputersManagerWindow.xaml`
- Modify: `ScreensView.Viewer/Views/ComputersManagerWindow.xaml.cs`
- Create: `ScreensView.Viewer/Views/ConnectionsFilePasswordWindow.xaml`
- Create: `ScreensView.Viewer/Views/ConnectionsFilePasswordWindow.xaml.cs`

- [ ] **Step 1: Remove `StartupUri` and build the manual startup flow**

Implement startup so `App`:
- loads `ViewerSettings`
- resolves the active source through `ConnectionsStorageController`
- prompts for a password when the controller reports that one is required
- only creates `MainWindow` after the active source is fully resolved

- [ ] **Step 2: Add the password dialog**

Implement a dialog that supports:
- “open existing encrypted file” mode with one password field and remember checkbox
- “create new encrypted file” mode with password, confirmation, and remember checkbox

- [ ] **Step 3: Add connections-source controls to `ComputersManagerWindow`**

Add:
- a label for the active source
- a `Файл подключений...` action for picking or creating an external file
- an `Использовать локальный файл подключений` action
- the security warning before external-file creation or selection

- [ ] **Step 4: Run verification for this wiring layer**

Run: `dotnet test ScreensView.Tests/ScreensView.Tests.csproj -v minimal`
Expected: PASS

Run: `dotnet build ScreensView.slnx -v minimal`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add ScreensView.Viewer/App.xaml ScreensView.Viewer/App.xaml.cs ScreensView.Viewer/MainWindow.xaml.cs ScreensView.Viewer/Views/ComputersManagerWindow.xaml ScreensView.Viewer/Views/ComputersManagerWindow.xaml.cs ScreensView.Viewer/Views/ConnectionsFilePasswordWindow.xaml ScreensView.Viewer/Views/ConnectionsFilePasswordWindow.xaml.cs
git commit -m "feat: wire encrypted connections source selection"
```

### Task 10: Update docs and run final verification

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/specs/2026-03-30-external-connections-file-design.md`
- Modify: `docs/superpowers/plans/2026-03-30-external-connections-file.md`

- [ ] **Step 1: Update `README.md`**

Document:
- the default local `%AppData%\ScreensView\computers.json` behavior
- how to select or create an encrypted external file
- when Viewer asks for a password
- what “remember on this computer” means
- how to return to the local file explicitly

- [ ] **Step 2: Reconcile plan/spec notes if implementation details changed**

Update the spec and this plan only if implementation diverged from the agreed design.

- [ ] **Step 3: Run the full verification suite**

Run: `dotnet test ScreensView.slnx -v minimal`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add README.md docs/superpowers/specs/2026-03-30-external-connections-file-design.md docs/superpowers/plans/2026-03-30-external-connections-file.md
git commit -m "docs: document encrypted connections source workflow"
```
