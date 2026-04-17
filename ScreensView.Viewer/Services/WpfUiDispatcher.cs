using System.Windows;

namespace ScreensView.Viewer.Services;

public class WpfUiDispatcher : IUiDispatcher
{
    public T Invoke<T>(Func<T> func) => Application.Current.Dispatcher.Invoke(func);
    public void Invoke(Action action) => Application.Current.Dispatcher.Invoke(action);
}
