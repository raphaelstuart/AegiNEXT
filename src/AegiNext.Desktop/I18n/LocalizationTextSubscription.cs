namespace AegiNext.Desktop.I18n;

internal sealed class LocalizationTextSubscription : IDisposable
{
    private readonly Func<string> textProvider;
    private readonly IObserver<string> observer;
    private bool disposed;

    internal LocalizationTextSubscription(Func<string> textProvider, IObserver<string> observer)
    {
        this.textProvider = textProvider;
        this.observer = observer;
        Localization.LanguageChanged += OnLanguageChanged;
        try
        {
            observer.OnNext(textProvider());
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Localization.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (!disposed)
        {
            observer.OnNext(textProvider());
        }
    }
}
