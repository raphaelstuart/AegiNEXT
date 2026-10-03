using Avalonia;

namespace AegiNext.Desktop.Windowing;

internal interface IWindowChrome : IDisposable
{
    void ResizeClient(Size size);
}
