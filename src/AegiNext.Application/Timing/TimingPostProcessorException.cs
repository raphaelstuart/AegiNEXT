namespace AegiNext.Application.Timing;

/// <summary>A timing batch could not produce a complete, valid project snapshot.</summary>
public sealed class TimingPostProcessorException : InvalidOperationException
{
    /// <summary>Describes a timing failure without exposing a partially edited document.</summary>
    public TimingPostProcessorException(string message, Exception? innerException = null) : base(message, innerException)
    {
    }
}
