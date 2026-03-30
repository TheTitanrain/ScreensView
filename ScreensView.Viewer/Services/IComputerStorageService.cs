using ScreensView.Shared.Models;

namespace ScreensView.Viewer.Services;

public interface IComputerStorageService
{
    List<ComputerConfig> Load();
    void Save(IEnumerable<ComputerConfig> computers);
}
