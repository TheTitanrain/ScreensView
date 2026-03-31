# Adaptive Tile Grid — Design Spec

**Date:** 2026-03-31
**Status:** Approved

## Problem

`MainWindow` displays computer screenshot tiles in a `WrapPanel` with hardcoded `Width="320" Height="210"`. When the window width does not perfectly fit an integer multiple of 320 px, empty space appears on the right side of the grid.

## Goal

Make the tile grid fill the full available width at all window sizes with no leftover space. The number of columns and tile dimensions must update automatically whenever the window is resized.

## Behavior

- Tiles fill the row uniformly — no empty horizontal space.
- The number of columns is computed from a minimum tile width of **240 px**: `columns = Math.Max(1, (int)(availableWidth / 240))`.
- Each tile's width is `availableWidth / columns` (exact, no remainder).
- Each tile's height scales proportionally to preserve the original **320 × 210** aspect ratio (`height = width × 210 / 320`).
- No manual size control — fully automatic on resize.

## Implementation

### XAML changes (`MainWindow.xaml`)

1. Add `x:Name="TileWrapPanel"` to the existing `WrapPanel`.
2. Add `SizeChanged="ScrollViewer_SizeChanged"` to the existing `ScrollViewer`.
3. Remove `Width="320" Height="210"` from the `Border` inside the `DataTemplate` — `WrapPanel.ItemWidth` / `ItemHeight` will control sizing uniformly.

### Code-behind (`MainWindow.xaml.cs`)

```csharp
private const double MinTileWidth = 240.0;
private const double TileAspectRatio = 210.0 / 320.0;

private void ScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
{
    var scrollViewer = (ScrollViewer)sender;
    double availableWidth = scrollViewer.ActualWidth;
    int columns = Math.Max(1, (int)(availableWidth / MinTileWidth));
    double tileWidth = availableWidth / columns;
    TileWrapPanel.ItemWidth = tileWidth;
    TileWrapPanel.ItemHeight = tileWidth * TileAspectRatio;
}
```

## Files Changed

- `ScreensView.Viewer/MainWindow.xaml` — XAML adjustments
- `ScreensView.Viewer/MainWindow.xaml.cs` — SizeChanged handler + constants

## Out of Scope

- Manual tile size slider
- Persisting tile size preference
- Changes to any other window
