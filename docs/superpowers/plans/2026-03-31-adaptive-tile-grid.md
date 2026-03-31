# Adaptive Tile Grid Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the screenshot tile grid in MainWindow fill the full available width at all window sizes with no empty space, using automatic column count and proportional tile sizing.

**Architecture:** On `ScrollViewer.SizeChanged`, compute the optimal column count from `ViewportWidth / 240` (minimum tile width), then set `WrapPanel.ItemWidth` and `ItemHeight` proportionally (aspect ratio 210/320). The `WrapPanel` instance is captured via its `Loaded` event (x:Name inside ItemsPanelTemplate is not reachable from code-behind). No MVVM changes needed — this is purely a view-layer concern.

**Tech Stack:** WPF / C# 12 / .NET 8

---

## File Map

| File | Change |
|------|--------|
| `ScreensView.Viewer/MainWindow.xaml` | Add `x:Name` + `SizeChanged` on ScrollViewer; add `Loaded` on WrapPanel; remove fixed `Width`/`Height` from tile Border |
| `ScreensView.Viewer/MainWindow.xaml.cs` | Add `using`, simplify existing Border cast, add `_tileWrapPanel` field and three methods |

No new files. No other files touched.

---

## Task 1: Update MainWindow.xaml

**Files:**
- Modify: `ScreensView.Viewer/MainWindow.xaml`

> Note: This change has no meaningful unit test — it is pure XAML layout wiring. Verification is a build check plus manual visual inspection.

- [ ] **Step 1: Add `x:Name` and `SizeChanged` to the ScrollViewer**

  In `MainWindow.xaml`, find the `ScrollViewer` on line 56:
  ```xml
  <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
  ```
  Change it to:
  ```xml
  <ScrollViewer x:Name="_screensScrollViewer"
                VerticalScrollBarVisibility="Auto"
                HorizontalScrollBarVisibility="Disabled"
                SizeChanged="ScrollViewer_SizeChanged">
  ```

- [ ] **Step 2: Add `Loaded` handler to the WrapPanel**

  Find the `WrapPanel` on line 60:
  ```xml
  <WrapPanel/>
  ```
  Change it to:
  ```xml
  <WrapPanel Loaded="WrapPanel_Loaded"/>
  ```

- [ ] **Step 3: Remove fixed dimensions from the tile Border**

  Find the `Border` on line 65:
  ```xml
  <Border Width="320" Height="210" Margin="6" BorderBrush="#CCCCCC" BorderThickness="1"
  ```
  Remove `Width="320" Height="210"` — keep everything else:
  ```xml
  <Border Margin="6" BorderBrush="#CCCCCC" BorderThickness="1"
  ```

- [ ] **Step 4: Build and verify no errors**

  ```bash
  dotnet build ScreensView.Viewer/ScreensView.Viewer.csproj
  ```
  Expected: build succeeds (event handlers not yet wired = compile errors at this point — that's fine, proceed to Task 2).

---

## Task 2: Update MainWindow.xaml.cs

**Files:**
- Modify: `ScreensView.Viewer/MainWindow.xaml.cs`

- [ ] **Step 1: Add `using System.Windows.Controls;`**

  At the top of the file, after the existing `using` directives, add:
  ```csharp
  using System.Windows.Controls;
  ```

- [ ] **Step 2: Simplify the existing fully-qualified Border cast**

  On line 50 (inside `Card_MouseLeftButtonDown`), find:
  ```csharp
  if (((System.Windows.Controls.Border)sender).DataContext is ComputerViewModel vm
  ```
  Replace with (now that the namespace is imported):
  ```csharp
  if (((Border)sender).DataContext is ComputerViewModel vm
  ```

- [ ] **Step 3: Add the `_tileWrapPanel` field and the three methods**

  Inside the `MainWindow` class, add after the existing fields (after line 13, before the constructor):
  ```csharp
  private const double MinTileWidth = 240.0;
  private const double TileAspectRatio = 210.0 / 320.0;

  private WrapPanel? _tileWrapPanel;
  ```

  At the end of the class (before the closing `}`), add:
  ```csharp
  private void WrapPanel_Loaded(object sender, RoutedEventArgs e)
  {
      _tileWrapPanel = (WrapPanel)sender;
      UpdateTileSize();
  }

  private void ScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
  {
      UpdateTileSize();
  }

  private void UpdateTileSize()
  {
      if (_tileWrapPanel == null) return;
      double availableWidth = _screensScrollViewer?.ViewportWidth ?? 0;
      if (availableWidth <= 0) return;
      int columns = Math.Max(1, (int)(availableWidth / MinTileWidth));
      double tileWidth = availableWidth / columns;
      _tileWrapPanel.ItemWidth = tileWidth;
      _tileWrapPanel.ItemHeight = tileWidth * TileAspectRatio;
  }
  ```

- [ ] **Step 4: Build**

  ```bash
  dotnet build ScreensView.Viewer/ScreensView.Viewer.csproj
  ```
  Expected: **Build succeeded** with 0 errors.

- [ ] **Step 5: Run the app and verify visually**

  ```bash
  dotnet run --project ScreensView.Viewer
  ```
  Checklist:
  - Tiles fill the full window width with no empty space on the right
  - Resizing the window updates columns and tile sizes in real time
  - At narrow window widths (e.g. 500px), tiles scale down gracefully (1 column minimum)
  - Bottom bar (name + status indicator + timestamp) remains visible on each tile
  - Double-clicking a tile still opens the zoom window

- [ ] **Step 6: Commit**

  ```bash
  git add ScreensView.Viewer/MainWindow.xaml ScreensView.Viewer/MainWindow.xaml.cs
  git commit -m "feat: adaptive tile grid fills full window width"
  ```
