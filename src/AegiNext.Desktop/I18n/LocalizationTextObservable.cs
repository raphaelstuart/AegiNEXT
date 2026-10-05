namespace AegiNext.Desktop.I18n;

internal sealed class LocalizationTextObservable(Func<string> textProvider) : IObservable<string>
{
    public IDisposable Subscribe(IObserver<string> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        return new LocalizationTextSubscription(textProvider, observer);
    }
}
