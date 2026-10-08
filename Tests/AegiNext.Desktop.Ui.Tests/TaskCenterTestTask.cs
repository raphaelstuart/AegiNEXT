using AegiNext.Application.Tasks;

namespace AegiNext.Desktop.Ui.Tests;

internal sealed class TaskCenterTestTask(string name, bool canCancel = true, AegiTaskMode mode = AegiTaskMode.Parallel,
    Exception? failure = null) : AegiTask
{
    public override string Name => name;
    public override bool CanCancel => canCancel;
    public override AegiTaskMode Mode => mode;
    internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Cleanup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal AegiTaskExecutionContext? Context { get; private set; }

    protected override async Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        Context = context;
        Started.TrySetResult();
        try
        {
            await Finish.Task.WaitAsync(context.CancellationToken);
            if (failure is not null)
            {
                throw failure;
            }
        }
        finally
        {
            await Cleanup.Task;
            Context = null;
        }
    }
}
