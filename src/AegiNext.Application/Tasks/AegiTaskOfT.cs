namespace AegiNext.Application.Tasks;

/// <summary>A scheduled business operation whose handle exposes its result.</summary>
public abstract class AegiTask<TResult> : AegiTask
{
    /// <summary>Executes the operation and returns its accepted result.</summary>
    protected abstract Task<TResult> ExecuteResultAsync(AegiTaskExecutionContext context);

    /// <inheritdoc />
    protected sealed override Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        return ExecuteResultAsync(context);
    }

    internal sealed override async Task<object?> ExecuteCoreAsync(AegiTaskExecutionContext context)
    {
        return await ExecuteResultAsync(context);
    }

    internal sealed override bool HasResult => true;
}
