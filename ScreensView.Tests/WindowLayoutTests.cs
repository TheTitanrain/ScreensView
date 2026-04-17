namespace ScreensView.Tests;

public class WindowLayoutTests
{
    [Fact]
    public void ComputersManagerWindow_HasEnoughDefaultWidthForToolbarActions()
    {
        var xaml = File.ReadAllText(GetRepoPath(@"ScreensView.Viewer\Views\ComputersManagerWindow.xaml"));

        Assert.DoesNotContain("MinWidth=\"900\"", xaml);
        Assert.Contains("x:Name=\"ToolbarPanel\"", xaml);
        Assert.Contains("x:Name=\"ConnectionsStatusPanel\"", xaml);
        Assert.DoesNotContain("Click=\"ConnectionsFile_Click\"", xaml);
        Assert.DoesNotContain("Click=\"UseLocal_Click\"", xaml);
    }

    [Fact]
    public void RefreshNowButton_IsHostedInMainWindowToolbar()
    {
        var mainWindowXaml = File.ReadAllText(GetRepoPath(@"ScreensView.Viewer\MainWindow.xaml"));
        var russianStrings = File.ReadAllText(GetRepoPath(@"ScreensView.Viewer\Resources\Strings.ru.xaml"));

        Assert.Contains("Command=\"{Binding RefreshNowCommand}\"", mainWindowXaml);
        Assert.Contains("Text=\"{DynamicResource Str.Toolbar.RefreshNow}\"", mainWindowXaml);
        Assert.Contains("x:Key=\"Str.Toolbar.RefreshNow\">Обновить сейчас<", russianStrings);
        Assert.DoesNotContain("Text=\"Интервал (сек):\"", mainWindowXaml);
        Assert.DoesNotContain("Content=\"Автозапуск\"", mainWindowXaml);
    }

    [Fact]
    public void LlmNowActions_AreHostedInToolbarAndTileContextMenu()
    {
        var mainWindowXaml = File.ReadAllText(GetRepoPath(@"ScreensView.Viewer\MainWindow.xaml"));
        var russianStrings = File.ReadAllText(GetRepoPath(@"ScreensView.Viewer\Resources\Strings.ru.xaml"));

        Assert.Contains("Command=\"{Binding RunLlmNowCommand}\"", mainWindowXaml);
        Assert.Contains("Text=\"{DynamicResource Str.Toolbar.LlmNow}\"", mainWindowXaml);
        Assert.Contains("Header=\"{DynamicResource Str.Menu.RunLlmNow}\"", mainWindowXaml);
        Assert.Contains("x:Key=\"Str.Toolbar.LlmNow\">LLM сейчас<", russianStrings);
        Assert.Contains("x:Key=\"Str.Menu.RunLlmNow\">Запустить LLM сейчас<", russianStrings);
        Assert.Contains("Click=\"TileMenu_RunLlmNow\"", mainWindowXaml);
    }

    [Fact]
    public void UpdateAllAgentsButton_IsHostedInComputersManagerWindowToolbar()
    {
        var mainWindowXaml = File.ReadAllText(GetRepoPath(@"ScreensView.Viewer\MainWindow.xaml"));
        var computersManagerXaml = File.ReadAllText(GetRepoPath(@"ScreensView.Viewer\Views\ComputersManagerWindow.xaml"));
        var russianStrings = File.ReadAllText(GetRepoPath(@"ScreensView.Viewer\Resources\Strings.ru.xaml"));

        Assert.DoesNotContain("Str.Computers.UpdateAll", mainWindowXaml);
        Assert.Contains("Click=\"UpdateAllAgents_Click\"", computersManagerXaml);
        Assert.Contains("Text=\"{DynamicResource Str.Computers.UpdateAll}\"", computersManagerXaml);
        Assert.Contains("x:Key=\"Str.Computers.UpdateAll\">Обновить агентов<", russianStrings);
    }

    [Fact]
    public void InstallDotNetRuntimesButton_IsHostedInComputersManagerWindowToolbar()
    {
        var mainWindowXaml = File.ReadAllText(GetRepoPath(@"ScreensView.Viewer\MainWindow.xaml"));
        var computersManagerXaml = File.ReadAllText(GetRepoPath(@"ScreensView.Viewer\Views\ComputersManagerWindow.xaml"));
        var russianStrings = File.ReadAllText(GetRepoPath(@"ScreensView.Viewer\Resources\Strings.ru.xaml"));

        Assert.DoesNotContain("Str.Computers.InstallDotNet", mainWindowXaml);
        Assert.Contains("Click=\"InstallDotNetRuntimes_Click\"", computersManagerXaml);
        Assert.Contains("Text=\"{DynamicResource Str.Computers.InstallDotNet}\"", computersManagerXaml);
        Assert.Contains("x:Key=\"Str.Computers.InstallDotNet\">Установить .NET 8 runtimes<", russianStrings);
    }

    [Fact]
    public void SettingsWindow_UsesSingleScrollableResizableLayout_WithGeneralAndStorageSections()
    {
        var settingsWindowXaml = File.ReadAllText(GetRepoPath(@"ScreensView.Viewer\Views\SettingsWindow.xaml"));
        var russianStrings = File.ReadAllText(GetRepoPath(@"ScreensView.Viewer\Resources\Strings.ru.xaml"));

        Assert.Contains("ResizeMode=\"CanResize\"", settingsWindowXaml);
        Assert.Contains("<ScrollViewer", settingsWindowXaml);
        Assert.Contains("Text=\"{DynamicResource Str.Settings.General}\"", settingsWindowXaml);
        Assert.Contains("Text=\"{DynamicResource Str.Settings.Llm}\"", settingsWindowXaml);
        Assert.Contains("Text=\"{DynamicResource Str.Settings.Connections}\"", settingsWindowXaml);
        Assert.Contains("x:Key=\"Str.Settings.General\">Общие<", russianStrings);
        Assert.Contains("x:Key=\"Str.Settings.Llm\">Распознавание экрана<", russianStrings);
        Assert.Contains("x:Key=\"Str.Settings.Connections\">Хранилище подключений<", russianStrings);
        Assert.Contains("Value=\"{Binding RefreshInterval}\"", settingsWindowXaml);
        Assert.Contains("IsChecked=\"{Binding IsAutostartEnabled}\"", settingsWindowXaml);
    }

    [Fact]
    public void AddEditComputerWindow_UsesSectionedResizableLayout()
    {
        var xaml = File.ReadAllText(GetRepoPath(@"ScreensView.Viewer\Views\AddEditComputerWindow.xaml"));
        var russianStrings = File.ReadAllText(GetRepoPath(@"ScreensView.Viewer\Resources\Strings.ru.xaml"));

        Assert.DoesNotContain("Height=\"480\" Width=\"420\"", xaml);
        Assert.Contains("<ScrollViewer", xaml);
        Assert.DoesNotContain("x:Name=\"HeaderTitleText\"", xaml);
        Assert.DoesNotContain("x:Name=\"HeaderDescriptionText\"", xaml);
        Assert.Contains("Text=\"{DynamicResource Str.AddEdit.ComputerSection}\"", xaml);
        Assert.Contains("Text=\"{DynamicResource Str.AddEdit.ConnSection}\"", xaml);
        Assert.Contains("Text=\"{DynamicResource Str.AddEdit.DescSection}\"", xaml);
        Assert.Contains("x:Key=\"Str.AddEdit.ComputerSection\">Компьютер<", russianStrings);
        Assert.Contains("x:Key=\"Str.AddEdit.ConnSection\">Подключение<", russianStrings);
        Assert.Contains("x:Key=\"Str.AddEdit.DescSection\">Описание экрана<", russianStrings);
        Assert.Contains("x:Name=\"HostPortRow\"", xaml);
        Assert.Contains("x:Name=\"PrimaryActionButton\"", xaml);
        Assert.DoesNotContain("Content=\"OK\"", xaml);
    }

    [Fact]
    public void AddEditComputerWindow_ExplainsHowToWriteScreenDescription()
    {
        var xaml = File.ReadAllText(GetRepoPath(@"ScreensView.Viewer\Views\AddEditComputerWindow.xaml"));
        var russianStrings = File.ReadAllText(GetRepoPath(@"ScreensView.Viewer\Resources\Strings.ru.xaml"));

        Assert.Contains("Text=\"{DynamicResource Str.AddEdit.DescHint}\"", xaml);
        Assert.Contains("Поле необязательное и нужно для сравнения текущего скриншота с ожидаемым типом экрана.", russianStrings);
        Assert.Contains("Опишите общий layout: крупные блоки, колонки, цветовые зоны.", russianStrings);
        Assert.Contains("Не указывайте точные времена, номера и фамилии.", russianStrings);
    }

    private static string GetRepoPath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relativePath));
}
