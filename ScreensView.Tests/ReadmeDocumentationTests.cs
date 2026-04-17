namespace ScreensView.Tests;

public class ReadmeDocumentationTests
{
    [Fact]
    public void Readme_ExplainsHowToWriteScreenDescription()
    {
        var readme = File.ReadAllText(GetRepoPath("README.ru.md"));

        Assert.Contains("Описание экрана помогает LLM сравнивать текущий скриншот с ожидаемым типом экрана.", readme);
        Assert.Contains("Пишите про стабильную визуальную структуру: крупные блоки, колонки, цветовые зоны, таймлайны, таблицы или плитки.", readme);
        Assert.Contains("Пример: `Табло с крупными номерами слева и широкими строками справа; важны общая структура, цветовые зоны и крупные интервалы времени.`", readme);
    }

    private static string GetRepoPath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relativePath));
}
