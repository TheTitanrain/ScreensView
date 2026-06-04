# Add Qwen3.5-9B to Supported Models Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **History:** Originally targeted `Qwen3.6-VL-2B`, which does not exist (unsloth's Qwen3.6 GGUFs start at 27B, text-only). Then `nvidia/LocateAnything-3B`, which is safetensors-only (no GGUF/mmproj) and incompatible with the llama-server pipeline. Final confirmed target: **`unsloth/Qwen3.5-9B-GGUF`** — a vision-capable GGUF with mmproj. URLs and sizes verified live (model 5.68 GB, mmproj 0.92 GB, both HTTP 200).

**Goal:** Add `Qwen3.5-9B` (Q4_K_M GGUF + F16 mmproj) to the list of selectable LLM screen-analysis models in the Viewer.

**Architecture:** The supported-model list is a static `IReadOnlyList<ModelDefinition>` in `ModelDefinition.Available`. The UI binds to this list, `ModelDownloadService` downloads `DownloadUrl` into `FileName` and `ProjectorDownloadUrl` into `ProjectorFileName`, and `LlamaServerLaunchProfile` already enables dynamic vision tokens for any filename containing "qwen" (case-insensitive). Adding a Qwen entry therefore requires no download-logic or launch-logic change — only a new record, a new test, and a README mention.

**Tech Stack:** C# / .NET 8 (`net8.0-windows`), xUnit, WPF.

---

## File Structure

- `ScreensView.Viewer/Models/ModelDefinition.cs` — append one `ModelDefinition` to the `Available` collection initializer. Single responsibility: the canonical model catalog.
- `ScreensView.Tests/ModelDefinitionTests.cs` — add one `[Fact]` asserting the new entry's exact id, display name, filenames, and URLs.
- `README.md` / `README.ru.md` — update the model-list sentence in the "screen analysis" usage section. These two files must always change together (project rule).

### Confirmed identifiers (verified against the live `unsloth/Qwen3.5-9B-GGUF` repo)

| Field | Value |
|---|---|
| `Id` | `qwen3.5-9b-q4` |
| `DisplayName` | `Qwen3.5-9B Q4_K_M (~5.7 + 0.9 GB)` |
| `FileName` | `Qwen3.5-9B-Q4_K_M.gguf` |
| `DownloadUrl` | `https://huggingface.co/unsloth/Qwen3.5-9B-GGUF/resolve/main/Qwen3.5-9B-Q4_K_M.gguf` |
| `ProjectorFileName` | `Qwen3.5-9B-mmproj-F16.gguf` |
| `ProjectorDownloadUrl` | `https://huggingface.co/unsloth/Qwen3.5-9B-GGUF/resolve/main/mmproj-F16.gguf` |

`ProjectorFileName` is a **distinct local on-disk name** (the remote file is the generic `mmproj-F16.gguf`); `ModelDownloadService` saves `ProjectorDownloadUrl` into `ProjectorFileName`, so a distinct local name avoids colliding with other models' projectors — the same pattern as the existing `qwen3.5-2b-q4` entry.

---

## Task 1: Verify the HuggingFace source resolves — DONE

Both resolve URLs were checked live and returned HTTP 200:
- `Qwen3.5-9B-Q4_K_M.gguf` → 200, 5.68 GB
- `mmproj-F16.gguf` → 200, 0.92 GB

- [x] **Step 1: Model GGUF URL responds (200)**
- [x] **Step 2: mmproj projector URL responds (200)**

---

## Task 2: Add the model definition

**Files:**
- Modify: `ScreensView.Viewer/Models/ModelDefinition.cs:33-37` (append after the `qwen3-vl-2b-q4` entry, inside the `Available` initializer)
- Test: `ScreensView.Tests/ModelDefinitionTests.cs`

- [ ] **Step 1: Write the failing test**

Add this method to the `ModelDefinitionTests` class in `ScreensView.Tests/ModelDefinitionTests.cs`:

```csharp
    [Fact]
    public void Available_ContainsQwen35_9BModel()
    {
        var qwen = Assert.Single(ModelDefinition.Available, model => model.Id == "qwen3.5-9b-q4");

        Assert.Equal("Qwen3.5-9B Q4_K_M (~5.7 + 0.9 GB)", qwen.DisplayName);
        Assert.Equal("Qwen3.5-9B-Q4_K_M.gguf", qwen.FileName);
        Assert.Equal(
            "https://huggingface.co/unsloth/Qwen3.5-9B-GGUF/resolve/main/Qwen3.5-9B-Q4_K_M.gguf",
            qwen.DownloadUrl);
        Assert.Equal("Qwen3.5-9B-mmproj-F16.gguf", qwen.ProjectorFileName);
        Assert.Equal(
            "https://huggingface.co/unsloth/Qwen3.5-9B-GGUF/resolve/main/mmproj-F16.gguf",
            qwen.ProjectorDownloadUrl);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run:
```powershell
dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter "FullyQualifiedName~Available_ContainsQwen35_9BModel"
```
Expected: FAIL — `Assert.Single` throws because no element matches `model.Id == "qwen3.5-9b-q4"` (the model isn't in the list yet).

- [ ] **Step 3: Add the model entry**

In `ScreensView.Viewer/Models/ModelDefinition.cs`, the current last entry in the `Available` collection initializer ends at line 37:

```csharp
        new("qwen3-vl-2b-q4", "Qwen3-VL-2B-Instruct Q4_K_M (~1.1 + 0.8 GB) [experimental]",
            "Qwen3-VL-2B-Instruct-Q4_K_M.gguf",
            "https://huggingface.co/unsloth/Qwen3-VL-2B-Instruct-GGUF/resolve/main/Qwen3-VL-2B-Instruct-Q4_K_M.gguf",
            "qwen3-vl-2b-instruct-mmproj-F16.gguf",
            "https://huggingface.co/unsloth/Qwen3-VL-2B-Instruct-GGUF/resolve/main/mmproj-F16.gguf"),
```

Add the new entry immediately after it (still inside the `[ ... ];` initializer), so the closing `];` follows the new entry:

```csharp
        new("qwen3.5-9b-q4", "Qwen3.5-9B Q4_K_M (~5.7 + 0.9 GB)",
            "Qwen3.5-9B-Q4_K_M.gguf",
            "https://huggingface.co/unsloth/Qwen3.5-9B-GGUF/resolve/main/Qwen3.5-9B-Q4_K_M.gguf",
            "Qwen3.5-9B-mmproj-F16.gguf",
            "https://huggingface.co/unsloth/Qwen3.5-9B-GGUF/resolve/main/mmproj-F16.gguf"),
```

- [ ] **Step 4: Run the test to verify it passes**

Run:
```powershell
dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter "FullyQualifiedName~Available_ContainsQwen35_9BModel"
```
Expected: PASS (1 passed).

- [ ] **Step 5: Run the full ModelDefinition test class to confirm no regressions**

Run:
```powershell
dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter "FullyQualifiedName~ModelDefinitionTests"
```
Expected: PASS — all tests pass (existing `Default_UsesLlavaV15Model` still passes; default model is unchanged).

- [ ] **Step 6: Commit**

```powershell
git add ScreensView.Viewer/Models/ModelDefinition.cs ScreensView.Tests/ModelDefinitionTests.cs
git commit -m @'
Add Qwen3.5-9B to supported models
'@
```

---

## Task 3: Update both README files

The two READMEs reference the model list with a generic example sentence and must stay in sync (edited together in one commit — project rule). The existing sentences mention `Qwen3.5-0.8B`; add `Qwen3.5-9B` to that example.

**Files:**
- Modify: `README.md:165`
- Modify: `README.ru.md:161`

- [ ] **Step 1: Update the English README**

In `README.md` line 165, replace:

```
2. On first use, pick a model and click **Download**. The default is a compatible `LLaVA v1.5 7B`. Experimental entries such as `Gemma 4 E2B`, `Qwen3.5-0.8B`, and other GGUF variants can also appear in Settings.
```

with:

```
2. On first use, pick a model and click **Download**. The default is a compatible `LLaVA v1.5 7B`. Other entries such as `Gemma 4 E2B`, `Qwen3.5-0.8B`, `Qwen3.5-9B`, and other GGUF variants can also appear in Settings.
```

- [ ] **Step 2: Update the Russian README**

In `README.ru.md` line 161, replace:

```
2. При первом запуске выберите модель и нажмите **Скачать**. По умолчанию выбран совместимый `LLaVA v1.5 7B`. В списке также доступны экспериментальные `Gemma 4 E2B`, `Qwen3.5-0.8B` и другие GGUF-варианты из настроек.
```

with:

```
2. При первом запуске выберите модель и нажмите **Скачать**. По умолчанию выбран совместимый `LLaVA v1.5 7B`. В списке также доступны `Gemma 4 E2B`, `Qwen3.5-0.8B`, `Qwen3.5-9B` и другие GGUF-варианты из настроек.
```

- [ ] **Step 3: Commit both READMEs together**

```powershell
git add README.md README.ru.md
git commit -m @'
Mention Qwen3.5-9B in README model list
'@
```

---

## Task 4: Final build verification

**Files:** none

- [ ] **Step 1: Build the Viewer to confirm the initializer compiles**

Run:
```powershell
dotnet build ScreensView.Viewer/ScreensView.Viewer.csproj
```
Expected: `Build succeeded` with 0 errors.

- [ ] **Step 2: Run the full test suite**

Run:
```powershell
dotnet test
```
Expected: All tests pass (the new `Available_ContainsQwen35_9BModel` test included).

---

## Notes / out of scope

- **No launch-profile change needed.** `LlamaServerLaunchProfile.cs:15` enables dynamic vision tokens for any filename containing "qwen" (case-insensitive); `Qwen3.5-9B-Q4_K_M.gguf` matches, so no change there.
- **No download-logic change needed.** `ModelDownloadService` downloads `ProjectorDownloadUrl` into the local `ProjectorFileName`; a distinct local name for a generic remote `mmproj-F16.gguf` is the established pattern (see `qwen3.5-2b-q4`).
- **Default model unchanged.** `ModelDefinition.Default => Available[0]` stays `llava-v1.5-7b-q4`. This plan only appends to the list.
- **Largest entry in the list.** At ~5.7 + 0.9 GB this is bigger than the current entries (0.5–4.1 GB); intentional, as the user requested the 9B model. Not marked `[experimental]`, matching the un-suffixed `Qwen3.5-2B` entry.
