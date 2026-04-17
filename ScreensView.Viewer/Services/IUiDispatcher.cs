namespace ScreensView.Viewer.Services;

public interface IUiDispatcher
{
    T Invoke<T>(Func<T> func);
    void Invoke(Action action);
}
