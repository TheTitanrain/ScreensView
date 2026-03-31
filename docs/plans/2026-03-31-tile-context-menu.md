---
# Context Menu for Tiles in MainWindow

## Overview
Add a right-click context menu to each computer tile in the main screenshot grid. Menu items: Открыть (zoom), Редактировать (edit), Пинг (health check), Удалить (delete).

## Context
- Files involved:
  - Modify: `ScreensView.Viewer/MainWindow.xaml` — add ContextMenu to tile Border
  - Modify: `ScreensView.Viewer/MainWindow.xaml.cs` — add menu item handlers
- Related patterns: existing code-behind pattern (Card_MouseLeftButtonDown, Edit_Click in ComputersManagerWindow)
- Dependencies: none new

## Development Approach
- Regular (code first)
- No new files needed — all changes are additions to existing files
- No tests: this is pure UI code-behind with no testable logic (same as existing card click handlers)

## Implementation Steps

### Task 1: Add ContextMenu XAML to tile Border

**Files:**
- Modify: `ScreensView.Viewer/MainWindow.xaml`

- [x] Add `<Border.ContextMenu>` block inside the tile `Border` (line ~207, the card border element)
- [x] Add menu item "Открыть" with Click="TileMenu_Open"
- [x] Add menu item "Редактировать" with Click="TileMenu_Edit"
- [x] Add Separator
- [x] Add menu item "Пинг" with Click="TileMenu_Ping"
- [x] Add Separator
- [x] Add menu item "Удалить" with Click="TileMenu_Delete"

### Task 2: Implement context menu handlers in MainWindow.xaml.cs

**Files:**
- Modify: `ScreensView.Viewer/MainWindow.xaml.cs`

- [ ] Add private helper `GetMenuVm(object sender)` — extracts ComputerViewModel from (MenuItem → ContextMenu → PlacementTarget → DataContext)
- [ ] Add `TileMenu_Open` — calls `new ScreenshotZoomWindow(vm) { Owner = this }.Show()` if status is not Locked
- [ ] Add `TileMenu_Edit` — opens `AddEditComputerWindow(vm.ToConfig())`, on OK calls `_vm.UpdateComputer(vm, win.Result)`
- [ ] Add `TileMenu_Ping` — creates `AgentHttpClient`, awaits `CheckHealthAsync(vm.ToConfig())`, shows result MessageBox (online/offline)
- [ ] Add `TileMenu_Delete` — shows confirm MessageBox, on Yes calls `_vm.RemoveComputer(vm)`

### Task 3: Verify acceptance criteria

- [ ] Manual test: right-click tile → menu appears with all 5 items
- [ ] Open: opens zoom window
- [ ] Edit: opens edit form, saves changes
- [ ] Ping: shows reachable/unreachable result
- [ ] Delete: confirms and removes tile
- [ ] Build: `dotnet build ScreensView.Viewer/ScreensView.Viewer.csproj` must succeed

### Task 4: Update documentation

- [ ] Move this plan to `docs/plans/completed/`
