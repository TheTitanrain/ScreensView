# Экран «О программе» — дизайн-документ

**Дата:** 2026-03-30
**Проект:** ScreensView.Viewer (WPF, net8.0-windows)

---

## Контекст

В приложении нет экрана «О программе». Пользователю нужно знать текущую версию, иметь быстрый доступ к GitHub-репозиторию, донат-ссылке и возможности вручную проверить обновления.

---

## Дизайн

### Триггер

Кнопка **«О программе»** добавляется в конец тулбара `MainWindow.xaml` (после `Separator`).

### `AboutWindow` (`Views/AboutWindow.xaml` + `.cs`)

Паттерн: стандартный WPF Window, как у `CredentialsDialog`:

- `Title="О программе"`, `Width="400"`, `ResizeMode="NoResize"`, `WindowStartupLocation="CenterOwner"`
- Код-бихайнд (не MVVM)

**Содержимое (сверху вниз):**

```
[Иконка приложения 64×64]  ScreensView  (крупный шрифт)
                            Версия: 1.0.0

© 2025 titanrain

[GitHub: github.com/titanrain/ScreensView]  (Hyperlink)
[Поддержать автора]                         (Hyperlink → https://donatr.ee/titanrain)

[Проверить обновления]          [Закрыть]
```

**Детали реализации:**

- Иконка: `<Image Source="/screensview.ico" Width="64" Height="64"/>` — тот же путь, что в `MainWindow.xaml Icon="/screensview.ico"`
- Версия: `Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "неизвестна"` (формат `X.Y.Z`)
- Ссылки: `Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })`
- Кнопка «Закрыть»: `IsCancel="True"`
- Кнопка «Проверить обновления»:
  - В обработчике: отключить кнопку (`IsEnabled = false`), вызвать `await ViewerUpdateService.CheckManualAsync(this)`, включить обратно (`IsEnabled = true`) в `finally`
  - Обработчик должен быть `async void`

### `ViewerUpdateService` — новый метод `CheckManualAsync(Window? owner)`

Добавить **новый статический метод** (не трогая `CheckAndUpdateAsync`):

```csharp
public static async Task CheckManualAsync(Window? owner = null)
```

**Содержит только логику проверки** (не копировать блок `--update-from`/`--install-to` из `CheckAndUpdateAsync` — он остаётся только в том методе).

Поведение:

- Если обновление найдено — MessageBox с предложением обновить (тот же текст, что сейчас в `CheckAndUpdateAsync`)
- Если обновление не найдено — `MessageBox.Show(owner, "Вы используете последнюю версию.", "Обновление ScreensView", ...)`
- Если ошибка — `MessageBox.Show(owner, "Не удалось проверить обновления.", "Обновление ScreensView", ...)` — **не глотать исключение молча**

Передавать `owner` во все вызовы `MessageBox.Show` внутри метода.

### `MainWindow` — изменения

**`MainWindow.xaml`:** добавить в конец `<ToolBar>`:
```xml
<Separator/>
<Button Content="О программе" Click="About_Click" Margin="4,0"/>
```

**`MainWindow.xaml.cs`:** добавить обработчик:
```csharp
private void About_Click(object sender, RoutedEventArgs e)
{
    new AboutWindow { Owner = this }.ShowDialog();
}
```

### Попутное исправление

В `ViewerUpdateService` заменить в константе `GitHubReleasesUrl` строку `YOUR_GITHUB_USER` на `titanrain`.

---

## Затрагиваемые файлы

| Файл | Изменение |
|------|-----------|
| `ScreensView.Viewer/Views/AboutWindow.xaml` | Новый файл |
| `ScreensView.Viewer/Views/AboutWindow.xaml.cs` | Новый файл |
| `ScreensView.Viewer/MainWindow.xaml` | +кнопка в тулбар |
| `ScreensView.Viewer/MainWindow.xaml.cs` | +обработчик About_Click |
| `ScreensView.Viewer/Services/ViewerUpdateService.cs` | +CheckManualAsync(Window?), fix URL |

---

## Проверка

1. `dotnet build ScreensView.Viewer` — без ошибок
2. Запустить Viewer, нажать «О программе» → окно открылось, версия отображается в формате X.Y.Z
3. Нажать «Проверить обновления» → кнопка блокируется на время запроса, появляется MessageBox с результатом
4. Кликнуть GitHub-ссылку → открывается браузер
5. Кликнуть «Поддержать автора» → браузер на [donatr.ee/titanrain](https://donatr.ee/titanrain)
6. «Закрыть» / Escape → окно закрывается


