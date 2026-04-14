# ClickOnce Publish Isolation for Viewer

**Date:** 2026-04-14  
**Status:** Implemented

## Goal

Устранить падение ClickOnce publish для `ScreensView.Viewer`, вызванное утечкой publish-свойств во вложенные сборки агентов и запуском legacy VS ClickOnce targets поверх уже готового SDK layout.

## Implemented Changes

- `ScreensView.Viewer/ScreensView.Viewer.csproj`
  - Зафиксирована изоляция nested `MSBuild` вызовов для `ScreensView.Agent` и `ScreensView.Agent.Legacy`.
  - Для nested build явно задаются `TargetFramework` (`net8.0-windows` и `net48`).
  - В `RemoveProperties` сохранён полный набор ClickOnce/publish свойств, которые не должны протекать во вложенные проекты.
  - Перед nested publish modern agent очищается `bin/<Configuration>/net8.0-windows/publish`.
  - В `CopyAgentPayloads` добавлены исключения для `*.application`, `setup.exe`, `Application Files\**`, `artifacts\**`, `*.pdb`.
- `ScreensView.Viewer/Directory.Build.targets`
  - Добавлены ClickOnce-условные overrides для legacy VS targets:
    - `_CopyFilesToPublishFolder`
    - `_DeploymentGenerateBootstrapper`
    - `_DeploymentSignClickOnceDeployment`
- `ScreensView.Viewer/Properties/PublishProfiles/ClickOnce.pubxml`
  - Зафиксирован репозиторный ClickOnce профиль Viewer.

## Verification Scenario

1. Visual Studio MSBuild publish:

```powershell
"C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe" `
  ScreensView.Viewer\ScreensView.Viewer.csproj `
  /t:Publish /p:Configuration=Release /p:PublishProfile=ClickOnce /v:minimal
```

Expected:
- publish completes without `MSB3094` and `MSB3030`.

2. SDK MSBuild publish:

```powershell
dotnet msbuild ScreensView.Viewer\ScreensView.Viewer.csproj `
  /t:Publish /p:Configuration=Release /p:PublishProfile=ClickOnce /v:minimal
```

Expected:
- publish completes successfully;
- output exists in `ScreensView.Viewer\bin\Release\net8.0-windows\app.publish\`.

3. Payload artifact checks:

Expected:
- `ScreensView.Viewer\bin\Release\net8.0-windows\AgentPayloads\Modern\ScreensView.Agent.exe` exists;
- `ScreensView.Viewer\bin\Release\net8.0-windows\AgentPayloads\Legacy\ScreensView.Agent.Legacy.exe` exists;
- `AgentPayloads\Modern` does not contain `setup.exe`, `*.application`, `Application Files\`.

4. Viewer ClickOnce artifacts:

Expected:
- `app.publish` contains Viewer ClickOnce artifacts, including `setup.exe` and `Application Files\`.
