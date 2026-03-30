# Настройка автозапуска Viewer — дизайн-документ

**Дата:** 2026-03-30
**Проект:** ScreensView.Viewer (WPF, net8.0-windows)

---

## Контекст

Сейчас у Viewer нет пользовательской настройки, которая включает или выключает автозапуск приложения при входе в Windows. Пользователю нужен простой переключатель в главном окне без отдельного окна настроек.

---

## Дизайн

### Триггер

В `MainWindow.xaml` в верхний `ToolBar` добавляется `CheckBox` с подписью **«Автозапуск»**.

### UI

- Элемент размещается рядом с другими глобальными действиями тулбара.
- Состояние чекбокса привязано к `MainViewModel.IsAutostartEnabled`.
- Переключение должно работать сразу, без дополнительной кнопки «Сохранить».

### Хранение настроек Viewer

Добавляется отдельный JSON-файл пользовательских настроек:

- путь: `%AppData%\ScreensView\viewer-settings.json`
- модель: `ViewerSettings`
- первое поле: `LaunchAtStartup`

`ViewerSettingsService` отвечает только за чтение и запись `viewer-settings.json`. Он не работает с Windows Registry.

### Интеграция с Windows autostart

Добавляется `AutostartService`, который управляет значением в:

`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`

Детали:

- имя значения: `ScreensView`
- данные значения: полный путь к текущему `.exe`, заключённый в кавычки
- включение: создать или обновить значение
- выключение: удалить значение

### Поведение `MainViewModel`

- При создании `MainViewModel`:
  - загрузить `ViewerSettings.LaunchAtStartup`
  - запросить фактическое состояние у `AutostartService`
  - если JSON и Registry расходятся, принять системное состояние как источник истины и сохранить его обратно в `viewer-settings.json`
- При изменении `IsAutostartEnabled`:
  - попытаться включить или выключить автозапуск через `AutostartService`
  - если операция успешна, сохранить новое состояние в `ViewerSettingsService`
  - если операция завершилась ошибкой, откатить свойство обратно и пробросить диагностическое сообщение в UI

### Обработка ошибок

- `MainViewModel` не должен показывать `MessageBox` напрямую.
- Для показа ошибок добавляется UI-колбэк, который передаётся в `MainViewModel` из `MainWindow`.
- Если запись в Registry не удалась:
  - пользователю показывается `MessageBox` с текстом ошибки
  - чекбокс возвращается в прежнее состояние
  - файл настроек не обновляется

---

## Затрагиваемые файлы

| Файл | Изменение |
|------|-----------|
| `ScreensView.Viewer/MainWindow.xaml` | +CheckBox автозапуска в toolbar |
| `ScreensView.Viewer/MainWindow.xaml.cs` | wiring `MainViewModel` с UI-ошибками |
| `ScreensView.Viewer/ViewModels/MainViewModel.cs` | состояние автозапуска и координация сервисов |
| `ScreensView.Viewer/Services/ViewerSettingsService.cs` | новый сервис чтения/записи viewer settings |
| `ScreensView.Viewer/Services/AutostartService.cs` | новый сервис работы с Windows Registry |
| `ScreensView.Tests/MainViewModelTests.cs` | TDD для включения/выключения автозапуска |
| `README.md` | описание новой настройки |

---

## Проверка

1. `dotnet test` проходит без ошибок.
2. В Viewer отображается чекбокс **«Автозапуск»**.
3. При включении чекбокса в `HKCU\...\Run` появляется значение `ScreensView`.
4. При выключении чекбокса значение `ScreensView` удаляется.
5. После перезапуска Viewer состояние чекбокса совпадает с фактическим состоянием автозапуска.
6. При ошибке записи в Registry чекбокс откатывается назад и пользователь видит сообщение об ошибке.
