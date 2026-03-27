# Screenshot Zoom Window

## Overview

Добавить возможность увеличить скриншот выбранного компьютера. Двойной клик по карточке открывает
`ScreenshotZoomWindow` — отдельное максимизированное окно без рамки, которое показывает живой поток
скриншота (обновляется вместе с polling-ом через binding).

**Проблема:** текущие карточки 320×220 слишком малы для детального просмотра.
**Решение:** переиспользовать тот же `ComputerViewModel` как `DataContext` нового окна — `Screenshot`
обновляется автоматически, никакой дополнительной логики не нужно.

## Context (from discovery)

- **Файлы UI:** `ScreensView.Viewer/MainWindow.xaml`, `ScreensView.Viewer/MainWindow.xaml.cs`
- **ViewModel:** `ScreensView.Viewer/ViewModels/ComputerViewModel.cs` — `ObservableObject` с `BitmapImage? Screenshot`, `Name`, `LastUpdated`, `Status`
- **Существующие окна для образца:** `ScreensView.Viewer/Views/` (ComputersManagerWindow, AddEditComputerWindow и др.)
- **Тема:** тёмная (`#1E1E1E` фон карточек, `#2D2D2D` footer)

## Development Approach

- **Testing approach:** Regular (код → тесты)
- Завершать каждую задачу полностью перед переходом к следующей
- UI-логика тривиальна (binding + window open/close), автотесты для неё не пишутся; тест-задача проверяет существующие тесты не сломаны

## Implementation Steps

### Task 1: Создать ScreenshotZoomWindow

**Files:**

- Create: `ScreensView.Viewer/Views/ScreenshotZoomWindow.xaml`
- Create: `ScreensView.Viewer/Views/ScreenshotZoomWindow.xaml.cs`

- [ ] Создать `ScreenshotZoomWindow.xaml`:
  - `WindowStyle="None"`, `WindowState="Maximized"`, `WindowStartupLocation="CenterOwner"`, `Background="#1E1E1E"`, `AllowsTransparency="False"`
  - Корневой `Grid` с двумя строками: `*` (изображение) и `28` (footer)
  - `Image Grid.Row="0" Source="{Binding Screenshot}" Stretch="Uniform" RenderOptions.BitmapScalingMode="HighQuality"`
  - Плейсхолдер `TextBlock Grid.Row="0" Text="Нет изображения"` с тем же `DataTrigger` на `{Binding Screenshot, Converter={StaticResource NullToBool}}` что и в карточках `MainWindow.xaml`
  - Кнопка `×` (Button) в `Grid.Row="0"`, `HorizontalAlignment="Right" VerticalAlignment="Top"`, `Panel.ZIndex="1"`
  - Footer `Border Background="#2D2D2D"` — статус-эллипс + `{Binding Name}` + `{Binding LastUpdated, StringFormat='HH:mm:ss'}`
- [ ] Создать `ScreenshotZoomWindow.xaml.cs`:
  - Конструктор принимает `ComputerViewModel vm`, устанавливает `DataContext = vm`
  - Обработчик `KeyDown`: закрывать окно по `Key.Escape`
  - Обработчик кнопки `×`: `Close()`
- [ ] `dotnet build` — убедиться, что сборка без ошибок

### Task 2: Подключить двойной клик в MainWindow

**Files:**

- Modify: `ScreensView.Viewer/MainWindow.xaml`
- Modify: `ScreensView.Viewer/MainWindow.xaml.cs`

- [ ] В DataTemplate карточки добавить на внешний `Border`:

  ```xml
  MouseDoubleClick="Card_MouseDoubleClick"
  ```

- [ ] В `MainWindow.xaml.cs` добавить обработчик:

  ```csharp
  private void Card_MouseDoubleClick(object sender, MouseButtonEventArgs e)
  {
      if (e.ChangedButton != MouseButton.Left) return;
      if (((Border)sender).DataContext is ComputerViewModel vm)
          new ScreenshotZoomWindow(vm) { Owner = this }.Show();
      e.Handled = true;
  }
  ```

  — `Show()` (не `ShowDialog()`), чтобы polling не блокировался; `Owner = this` — окно открывается на том же монитое, что и главное; левая кнопка мыши — защита от правого клика

- [ ] `dotnet build` — убедиться, что сборка без ошибок

### Task 3: Проверить, что сборка и тесты не сломаны

**Files:** (нет изменений)

- [ ] `dotnet build` — убедиться, что сборка без ошибок
- [ ] `dotnet test` — убедиться, что существующие тесты зелёные

### Task 4: [Final] Завершение

- [ ] Проверить UX вручную: двойной клик → окно открывается; живой поток обновляется; ESC и × закрывают; несколько окон одновременно работают
- [ ] Переместить план в `docs/plans/completed/`

## Technical Details

**Почему `Show()` а не `ShowDialog()`:** `Show()` создаёт modeless-окно — можно открывать несколько зум-окон одновременно для разных компьютеров. `ShowDialog()` заблокировало бы MainWindow до закрытия.

**Живые обновления:** `ComputerViewModel.Screenshot` — `[ObservableProperty]` (`CommunityToolkit.Mvvm`). `ScreenshotZoomWindow` получает тот же экземпляр VM как `DataContext`. WPF binding автоматически подписывается на `PropertyChanged` и обновляет `Image.Source` без дополнительного кода.

**Stale screenshot при offline:** `SetError()` не очищает `Screenshot` — устаревший кадр остаётся видным. Это допустимо: статус компьютера виден через цвет индикатора в footer. Очищать `Screenshot` при ошибке не нужно.

**BitmapScalingMode:** в карточках `LowQuality` (быстро). В окне зума используем `HighQuality` (лучший результат при растяжении на весь экран).

**Несколько окон:** каждый двойной клик создаёт новый экземпляр `ScreenshotZoomWindow`. Это разрешено и удобно при мультимониторной работе.

**Монитор при открытии:** `Owner = this` + `WindowStartupLocation="CenterOwner"` дают WPF подсказку разместить окно на том же экране, где находится MainWindow. Точное поведение на мультимониторных конфигурациях проверяется вручную (Post-Completion).

## Post-Completion

**Ручная проверка:**

- Открыть несколько зум-окон для разных компьютеров и убедиться, что все обновляются независимо
- Проверить на мультимониторной конфигурации
- Проверить поведение при отключении компьютера (статус меняется в обоих окнах)
