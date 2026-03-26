# ScreensView

Система мониторинга экранов компьютеров в локальной сети. Агент на каждой машине отдаёт скриншот по HTTPS, наблюдатель отображает все экраны в сетке.

## Структура решения

| Проект | Назначение |
|---|---|
| `ScreensView.Agent` | Modern Windows Service (`.NET 8`) для Windows 10/11 и поддерживаемых серверных ОС |
| `ScreensView.Agent.Legacy` | Legacy Windows Service (`.NET Framework 4.8`) для Windows 7 SP1 |
| `ScreensView.Viewer` | WPF-приложение — сетка скриншотов, управление компьютерами |
| `ScreensView.Shared` | Общие модели, константы и JSON-контракт агента |

## Требования

- **Viewer**: `.NET 8`, Windows 10/11
- **Modern Agent**: `.NET 8`, Windows 10/11
- **Legacy Agent**: Windows 7 SP1 и установленный `.NET Framework 4.8`
- Для агента: права администратора (сертификат и HTTPS binding в системе)
- Для удалённой установки: права локального администратора на целевой машине, открытый `Admin$`, доступный WMI (порт 135 + dynamic RPC)

> Windows 7 support — это compatibility path. В `2026` году он не считается поддерживаемой Microsoft платформой: `.NET Framework 4.8` следует lifecycle базовой Windows OS.

## Быстрый старт

### 1. Настройка агента

Оба варианта агента используют один и тот же `appsettings.json`:

```json
{
  "Agent": {
    "Port": 5443,
    "ApiKey": "ВАШ_СЕКРЕТНЫЙ_КЛЮЧ",
    "ScreenshotQuality": 75
  }
}
```

### 2. Modern Agent (`Windows 10/11`)

**Запуск для тестирования** (от администратора):
```
dotnet run --project ScreensView.Agent
```

**Установка как Windows Service** (от администратора):
```
sc create ScreensViewAgent binPath= "C:\путь\к\ScreensView.Agent.exe" start= auto
sc start ScreensViewAgent
```

### 3. Legacy Agent (`Windows 7 SP1`)

**Сборка legacy-агента**:
```
dotnet build ScreensView.Agent.Legacy/ScreensView.Agent.Legacy.csproj
```

**Установка как Windows Service** (от администратора):
```
sc create ScreensViewAgent binPath= "C:\путь\к\ScreensView.Agent.Legacy.exe" start= auto
sc start ScreensViewAgent
```

Legacy-агент сам создаёт self-signed сертификат и на старте обновляет HTTPS binding через `netsh http`.

### 4. Проверка агента

```
curl -k -H "X-Api-Key: ВАШ_СЕКРЕТНЫЙ_КЛЮЧ" https://localhost:5443/health
```

### 5. Viewer

```
dotnet run --project ScreensView.Viewer
```

1. Нажать **Управление компьютерами → Добавить**
2. Заполнить имя, IP, порт, API-ключ
3. Нажать **Запустить** — скриншоты появятся в сетке

## Удалённая установка агента

В окне «Управление компьютерами» выбрать компьютер → **Установить агент**.

Viewer автоматически выбирает payload по целевой ОС:

- `Windows 10/11` и поддерживаемые серверные ОС → `ScreensView.Agent` (`.NET 8`)
- `Windows 7 SP1` + `.NET Framework 4.8` → `ScreensView.Agent.Legacy` (`net48`)
- `Windows 7` без `.NET Framework 4.8` → установка прерывается с диагностической ошибкой
- неподдерживаемые ОС (например, старые workstation-версии между Windows 7 и Windows 10) → установка прерывается с явным сообщением

Требования к целевой машине:

- открыт `Admin$` (`\\hostname\Admin$`)
- разрешён WMI (порт `135` + dynamic RPC)
- учётная запись с правами локального администратора

Viewer копирует выбранный payload в `C:\Windows\ScreensViewAgent\`, создаёт и запускает службу `ScreensViewAgent`.

## Безопасность

| Угроза | Защита |
|---|---|
| Перехват трафика | HTTPS (TLS 1.2+), self-signed сертификат |
| Несанкционированный доступ к агенту | API-ключ в заголовке `X-Api-Key` |
| Подмена сертификата | Pinning SHA-256 thumbprint в Viewer |

Сертификат генерируется автоматически при первом запуске агента и хранится в `LocalMachine\My`.

## Обновление

### Агент (из Viewer)

Выбрать компьютер → **Управление компьютерами** → остановит службу, заменит payload, затем снова запустит службу. Кнопка **Обновить агентов** в toolbar обновляет все машины параллельно, сохраняя выбор `modern`/`legacy` по ОС.

### Viewer (авто-обновление)

При запуске Viewer проверяет GitHub Releases. Если есть новая версия — предлагает обновиться. Для включения укажи репозиторий в `ScreensView.Viewer/Services/ViewerUpdateService.cs`:

```csharp
private const string GitHubReleasesUrl =
    "https://api.github.com/repos/YOUR_GITHUB_USER/ScreensView/releases/latest";
```

## Настройка агента

| Параметр | По умолчанию | Описание |
|---|---|---|
| `Agent:Port` | `5443` | HTTPS-порт |
| `Agent:ApiKey` | *(обязательно)* | Секретный ключ авторизации |
| `Agent:ScreenshotQuality` | `75` | Качество JPEG (1–100) |
