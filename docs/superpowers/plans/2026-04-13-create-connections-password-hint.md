# Create Connections Password Hint Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the create-connections-file password dialog explicitly say that the remembered password is stored only in encrypted form.

**Architecture:** Keep the existing dialog layout and visibility rules unchanged. Adjust only the hint text selection in `ConnectionsFilePasswordWindow` so `CreateNew` shows the stronger wording while `OpenExisting` preserves its current copy.

**Tech Stack:** C#, WPF, xUnit

---

### Task 1: Add a failing UI test for the create dialog hint

**Files:**
- Modify: `ScreensView.Tests/ConnectionsFilePasswordWindowTests.cs`
- Test: `ScreensView.Tests/ConnectionsFilePasswordWindowTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void CreateMode_ShowsEncryptedRememberPasswordHint()
{
    var hintText = RunOnSta(() =>
    {
        var window = new ConnectionsFilePasswordWindow(
            ConnectionsFilePasswordMode.CreateNew,
            @"C:\Shared\connections.svc",
            allowRememberPassword: true);

        return GetElement<TextBlock>(window, "RememberPasswordHint").Text;
    });

    Assert.Contains("зашифрован", hintText, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ScreensView.Tests\ScreensView.Tests.csproj --filter CreateMode_ShowsEncryptedRememberPasswordHint`
Expected: FAIL because the current create dialog hint does not mention encrypted storage.

### Task 2: Implement the minimal hint selection change

**Files:**
- Modify: `ScreensView.Viewer/Views/ConnectionsFilePasswordWindow.xaml.cs`
- Test: `ScreensView.Tests/ConnectionsFilePasswordWindowTests.cs`

- [ ] **Step 1: Write minimal implementation**

```csharp
RememberPasswordHint.Text = mode == ConnectionsFilePasswordMode.CreateNew
    ? "Пароль сохраняется только локально для текущего пользователя Windows и только в зашифрованном виде."
    : "Пароль сохраняется только локально для текущего пользователя Windows.";
```

- [ ] **Step 2: Run test to verify it passes**

Run: `dotnet test ScreensView.Tests\ScreensView.Tests.csproj --filter CreateMode_ShowsEncryptedRememberPasswordHint`
Expected: PASS

### Task 3: Verify and commit

**Files:**
- Modify: `ScreensView.Viewer/Views/ConnectionsFilePasswordWindow.xaml.cs`
- Modify: `ScreensView.Tests/ConnectionsFilePasswordWindowTests.cs`

- [ ] **Step 1: Run the focused dialog tests**

Run: `dotnet test ScreensView.Tests\ScreensView.Tests.csproj --filter ConnectionsFilePasswordWindowTests`
Expected: PASS

- [ ] **Step 2: Run the full test project**

Run: `dotnet test ScreensView.Tests\ScreensView.Tests.csproj --logger "trx;LogFileName=create-password-hint.trx"`
Expected: PASS with `failed="0"` in the `.trx` counters.

- [ ] **Step 3: Commit**

```bash
git add ScreensView.Viewer/Views/ConnectionsFilePasswordWindow.xaml.cs ScreensView.Tests/ConnectionsFilePasswordWindowTests.cs docs/superpowers/plans/2026-04-13-create-connections-password-hint.md
git commit -m "Clarify encrypted password storage in create dialog"
```
