# ScreensView

Система мониторинга экранов компьютеров в локальной сети. Агент на каждой машине отдаёт скриншот по HTTPS, наблюдатель отображает все экраны в сетке.

## Структура решения

| Проект | Назначение |
|---|---|
| `ScreensView.Agent` | Modern Windows Service (`.NET 8`) для Windows 10/11 и поддерживаемых серверных ОС |
| `ScreensView.Agent.Legacy` | Legacy Windows Service (`.NET Framework 4.8`) для Windows 7 SP1 |
| `ScreensView.Viewer` | WPF-приложение — сетка скриншотов, управление компьютерами |
| `ScreensView.Shared` | Общие модели, константы и JSON-контракт агента |
| `ScreensView.Tests` | xUnit-тесты: протокол именованного пайпа, `NoActiveSessionException` |

## Требования

- **Viewer**: `.NET 8`, Windows 10/11
- **Modern Agent**: `.NET 8`, Windows 10/11
- **Legacy Agent**: Windows 7 SP1 и установленный `.NET Framework 4.8`
- Для агента: права администратора (сертификат и HTTPS binding в системе)
- **Оба агента запускаются под `LocalSystem`** — это требование `WTSQueryUserToken` (захват экрана из Windows Service). Сервис, запущенный под другой учётной записью, получит ошибку при попытке сделать скриншот (`WTSQueryUserToken` требует привилегию `SeTcbPrivilege`, которая есть только у LocalSystem)
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

> При отсутствии активного пользователя на консоли или при заблокированной рабочей станции `/screenshot` возвращает **HTTP 503** вместо скриншота. Это штатное поведение: viewer помечает такую машину как `Locked` и показывает оверлей с замком, потому что агент не может захватить изображение с secure desktop.

### 5. Viewer

```
dotnet run --project ScreensView.Viewer
```

1. Нажать **Управление компьютерами → Добавить**
2. Заполнить имя, IP, порт, API-ключ
3. Нажать **Запустить** — скриншоты появятся в сетке
4. При необходимости включить **Автозапуск** в верхнем toolbar, чтобы Viewer запускался вместе с Windows

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

Окно установки/обновления/удаления отображает каждый шаг в реальном времени с цветовой индикацией: зелёный — успех, красный — ошибка, жёлтый — предупреждение (например, служба не смогла остановиться, но операция продолжена).

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

При запуске Viewer проверяет GitHub Releases. Если есть новая версия — предлагает обновиться. Та же проверка доступна вручную в окне «О программе» по кнопке «Проверить обновления». В проекте используется такой репозиторий в `ScreensView.Viewer/Services/ViewerUpdateService.cs`:

```csharp
private const string GitHubReleasesUrl =
    "https://api.github.com/repos/titanrain/ScreensView/releases/latest";
```

### Настройки Viewer

Viewer хранит локальные настройки в `%AppData%\ScreensView\viewer-settings.json`.

- Слайдер **Интервал (сек)** в toolbar сохраняет последнее выбранное значение в `RefreshIntervalSeconds` и восстанавливает его при следующем запуске Viewer.
- Чекбокс **Автозапуск** в toolbar включает или выключает запуск Viewer при входе в Windows.
- На Windows это соответствует значению `ScreensView` в `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

### Хранение подключений

По умолчанию список компьютеров Viewer хранит локально в `%AppData%\\ScreensView\\computers.json`.

- `Name`, `Host`, `Port`, `IsEnabled` и `CertThumbprint` сохраняются в JSON как часть локальной конфигурации подключения.
- `ApiKey` в локальном файле не хранится открытым текстом: вместо него записывается `ApiKeyEncrypted`, зашифрованный через Windows DPAPI для текущего пользователя.
- В storage-слое Viewer также поддерживается отдельный зашифрованный контейнер подключений для внешних файлов. Он содержит только `Version`, `KdfSalt`, `Nonce`, `Tag` и `Ciphertext`.
- Для внешнего контейнера используется `PBKDF2-SHA256` для derivation ключа из пароля и `AES-GCM` для шифрования всего списка `ComputerConfig` целиком.

## Настройка агента

| Параметр | По умолчанию | Описание |
|---|---|---|
| `Agent:Port` | `5443` | HTTPS-порт |
| `Agent:ApiKey` | *(обязательно)* | Секретный ключ авторизации |
| `Agent:ScreenshotQuality` | `75` | Качество JPEG (1–100) |
