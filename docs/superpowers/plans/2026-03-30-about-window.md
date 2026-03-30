# About Window Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an «О программе» dialog accessible via a toolbar button, displaying app name, version, copyright, GitHub/donate links, and a manual update check button.

**Architecture:** New `AboutWindow` (code-behind, no MVVM) following the existing `CredentialsDialog` pattern. `ViewerUpdateService` gets a new `CheckManualAsync(Window?)` static method for manual update checks with user-visible feedback. `MainWindow` toolbar gets a single button trigger.

**Tech Stack:** WPF / net8.0-windows, CommunityToolkit.Mvvm (not used in this feature), `System.Reflection.Assembly`, `System.Diagnostics.Process`

---

## File Map

| File | Action | Responsibility |
|------|--------|----------------|
| `ScreensView.Viewer/Services/ViewerUpdateService.cs` | Modify | Add `CheckManualAsync(Window?)`, fix GitHub URL constant |
| `ScreensView.Viewer/Views/AboutWindow.xaml` | Create | About dialog layout |
| `ScreensView.Viewer/Views/AboutWindow.xaml.cs` | Create | About dialog code-behind |
| `ScreensView.Viewer/MainWindow.xaml` | Modify | Add «О программе» button to toolbar |
| `ScreensView.Viewer/MainWindow.xaml.cs` | Modify | Add `About_Click` handler |

---

## Task 1: Extend ViewerUpdateService

**Files:**
- Modify: `ScreensView.Viewer/Services/ViewerUpdateService.cs`

- [ ] **Step 1.1: Fix the GitHub URL constant**

  In `ViewerUpdateService.cs`, replace the placeholder owner in `GitHubReleasesUrl`:

  ```csharp
  // Before:
  private const string GitHubReleasesUrl =
      "https://api.github.com/repos/YOUR_GITHUB_USER/ScreensView/releases/latest";

  // After:
  private const string GitHubReleasesUrl =
      "https://api.github.com/repos/titanrain/ScreensView/releases/latest";
  ```

- [ ] **Step 1.2: Add `CheckManualAsync` method**

  Add this method to `ViewerUpdateService` after `CheckAndUpdateAsync`. It contains **only** the update-check logic — the `--update-from`/`--install-to` argument block stays exclusively in `CheckAndUpdateAsync`.

  ```csharp
  public static async Task CheckManualAsync(Window? owner = null)
  {
      try
      {
          using var http = new HttpClient();
          http.DefaultRequestHeaders.Add("User-Agent", "ScreensView");
          http.Timeout = TimeSpan.FromSeconds(10);

          var release = await http.GetFromJsonAsync<GitHubRelease>(GitHubReleasesUrl);
          if (release == null)
          {
              MessageBox.Show(owner, "Не удалось проверить обновления.", "Обновление ScreensView",
                  MessageBoxButton.OK, MessageBoxImage.Warning);
              return;
          }

          var latestVersion  = ParseVersion(release.TagName);
          var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);

          if (latestVersion <= currentVersion)
          {
              MessageBox.Show(owner, "Вы используете последнюю версию.", "Обновление ScreensView",
                  MessageBoxButton.OK, MessageBoxImage.Information);
              return;
          }

          var downloadUrl = release.Assets?.FirstOrDefault(a =>
              a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))?.BrowserDownloadUrl;
          if (string.IsNullOrEmpty(downloadUrl))
          {
              MessageBox.Show(owner, "Не удалось проверить обновления.", "Обновление ScreensView",
                  MessageBoxButton.OK, MessageBoxImage.Warning);
              return;
          }

          var result = MessageBox.Show(owner,
              $"Доступна новая версия {release.TagName}.\nТекущая версия: {currentVersion}\n\nОбновить сейчас?",
              "Обновление ScreensView", MessageBoxButton.YesNo, MessageBoxImage.Information);
          if (result != MessageBoxResult.Yes) return;

          var originalPath = Environment.ProcessPath!;
          var tempPath     = originalPath + ".download.exe";

          var bytes = await http.GetByteArrayAsync(downloadUrl);
          await File.WriteAllBytesAsync(tempPath, bytes);

          var launchArgs = $"--update-from \"{tempPath}\" --install-to \"{originalPath}\"";
          Process.Start(new ProcessStartInfo(tempPath, launchArgs) { UseShellExecute = true });
          Application.Current.Shutdown();
      }
      catch
      {
          MessageBox.Show(owner, "Не удалось проверить обновления.", "Обновление ScreensView",
              MessageBoxButton.OK, MessageBoxImage.Warning);
      }
  }
  ```

  Note: This method shows a message in **all** outcomes — unlike `CheckAndUpdateAsync`, which silently ignores errors at startup.

- [ ] **Step 1.3: Build to verify no errors**

  ```bash
  dotnet build ScreensView.Viewer/ScreensView.Viewer.csproj
  ```

  Expected: `Build succeeded.`

- [ ] **Step 1.4: Commit**

  ```bash
  git add ScreensView.Viewer/Services/ViewerUpdateService.cs
  git commit -m "feat: add CheckManualAsync with visible feedback; fix GitHub owner URL"
  ```

---

## Task 2: Create AboutWindow

**Files:**
- Create: `ScreensView.Viewer/Views/AboutWindow.xaml`
- Create: `ScreensView.Viewer/Views/AboutWindow.xaml.cs`

- [ ] **Step 2.1: Create `AboutWindow.xaml`**

  Create `ScreensView.Viewer/Views/AboutWindow.xaml`:

  ```xml
  <Window x:Class="ScreensView.Viewer.Views.AboutWindow"
          xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
          Title="О программе"
          Width="400"
          ResizeMode="NoResize"
          SizeToContent="Height"
          WindowStartupLocation="CenterOwner">
      <Grid Margin="24">
          <Grid.RowDefinitions>
              <RowDefinition Height="Auto"/>
              <RowDefinition Height="Auto"/>
              <RowDefinition Height="Auto"/>
              <RowDefinition Height="Auto"/>
          </Grid.RowDefinitions>

          <!-- App icon + name + version -->
          <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,0,0,16">
              <Image Source="/screensview.ico" Width="64" Height="64" VerticalAlignment="Center"/>
              <StackPanel VerticalAlignment="Center" Margin="16,0,0,0">
                  <TextBlock x:Name="AppNameText" FontSize="22" FontWeight="Bold"/>
                  <TextBlock x:Name="VersionText" Foreground="#666" Margin="0,4,0,0"/>
              </StackPanel>
          </StackPanel>

          <!-- Copyright -->
          <TextBlock Grid.Row="1" x:Name="CopyrightText" Foreground="#888" Margin="0,0,0,16"/>

          <!-- Links -->
          <StackPanel Grid.Row="2" Margin="0,0,0,24">
              <TextBlock Margin="0,0,0,6">
                  <Hyperlink x:Name="GitHubLink" RequestNavigate="Link_RequestNavigate">
                      github.com/titanrain/ScreensView
                  </Hyperlink>
              </TextBlock>
              <TextBlock>
                  <Hyperlink x:Name="DonateLink" RequestNavigate="Link_RequestNavigate">
                      Поддержать автора
                  </Hyperlink>
              </TextBlock>
          </StackPanel>

          <!-- Buttons -->
          <StackPanel Grid.Row="3" Orientation="Horizontal" HorizontalAlignment="Right">
              <Button x:Name="CheckUpdateButton" Content="Проверить обновления"
                      Width="170" Margin="0,0,8,0"
                      Click="CheckUpdate_Click"/>
              <Button Content="Закрыть" Width="96" IsCancel="True"/>
          </StackPanel>
      </Grid>
  </Window>
  ```

- [ ] **Step 2.2: Create `AboutWindow.xaml.cs`**

  Create `ScreensView.Viewer/Views/AboutWindow.xaml.cs`:

  ```csharp
  using System.Diagnostics;
  using System.Reflection;
  using System.Windows;
  using System.Windows.Navigation;
  using ScreensView.Viewer.Services;

  namespace ScreensView.Viewer.Views;

  public partial class AboutWindow : Window
  {
      public AboutWindow()
      {
          InitializeComponent();

          AppNameText.Text = "ScreensView";

          var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "неизвестна";
          VersionText.Text = $"Версия: {version}";

          CopyrightText.Text = "© 2025 titanrain";

          GitHubLink.NavigateUri = new Uri("https://github.com/titanrain/ScreensView");
          DonateLink.NavigateUri = new Uri("https://donatr.ee/titanrain");
      }

      private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
      {
          Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
          e.Handled = true;
      }

      private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
      {
          CheckUpdateButton.IsEnabled = false;
          try
          {
              await ViewerUpdateService.CheckManualAsync(this);
          }
          finally
          {
              CheckUpdateButton.IsEnabled = true;
          }
      }
  }
  ```

- [ ] **Step 2.3: Build to verify no errors**

  ```bash
  dotnet build ScreensView.Viewer/ScreensView.Viewer.csproj
  ```

  Expected: `Build succeeded.`

- [ ] **Step 2.4: Commit**

  ```bash
  git add ScreensView.Viewer/Views/AboutWindow.xaml ScreensView.Viewer/Views/AboutWindow.xaml.cs
  git commit -m "feat: add AboutWindow dialog"
  ```

---

## Task 3: Wire Up Toolbar Button in MainWindow

**Files:**
- Modify: `ScreensView.Viewer/MainWindow.xaml`
- Modify: `ScreensView.Viewer/MainWindow.xaml.cs`

- [ ] **Step 3.1: Add button to toolbar in `MainWindow.xaml`**

  In `MainWindow.xaml`, the `<ToolBar>` ends with:
  ```xml
  <Button Content="Обновить агентов" Click="UpdateAllAgents_Click" Margin="4,0"/>
  ```

  Append after that button (still inside `<ToolBar>`):
  ```xml
  <Separator/>
  <Button Content="О программе" Click="About_Click" Margin="4,0"/>
  ```

- [ ] **Step 3.2: Add `About_Click` handler in `MainWindow.xaml.cs`**

  Add this method to `MainWindow.xaml.cs` alongside the other click handlers:

  ```csharp
  private void About_Click(object sender, RoutedEventArgs e)
  {
      new AboutWindow { Owner = this }.ShowDialog();
  }
  ```

  Also add the using at the top if not already present:
  ```csharp
  using ScreensView.Viewer.Views;
  ```

- [ ] **Step 3.3: Build to verify no errors**

  ```bash
  dotnet build ScreensView.Viewer/ScreensView.Viewer.csproj
  ```

  Expected: `Build succeeded.`

- [ ] **Step 3.4: Commit**

  ```bash
  git add ScreensView.Viewer/MainWindow.xaml ScreensView.Viewer/MainWindow.xaml.cs
  git commit -m "feat: add О программе toolbar button wired to AboutWindow"
  ```

---

## Verification Checklist

Run the app (`dotnet run --project ScreensView.Viewer`) and verify:

- [ ] Toolbar shows «О программе» button at the right end
- [ ] Clicking it opens a centered dialog titled «О программе»
- [ ] Dialog shows app name «ScreensView», version in X.Y.Z format, «© 2025 titanrain»
- [ ] Clicking GitHub link opens browser at `https://github.com/titanrain/ScreensView`
- [ ] Clicking «Поддержать автора» opens browser at `https://donatr.ee/titanrain`
- [ ] Clicking «Проверить обновления» disables the button, shows a MessageBox result, re-enables button
- [ ] Pressing Escape / clicking «Закрыть» closes the dialog
