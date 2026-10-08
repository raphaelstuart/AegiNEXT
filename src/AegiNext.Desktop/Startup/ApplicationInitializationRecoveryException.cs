namespace AegiNext.Desktop.Startup;

internal sealed class ApplicationInitializationRecoveryException(IEnumerable<Exception> errors)
    : AggregateException("Application initialization completed with recoverable storage errors.", errors);
