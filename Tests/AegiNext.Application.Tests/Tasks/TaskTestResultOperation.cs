using AegiNext.Application.Tasks;

namespace AegiNext.Application.Tests.Tasks;

internal sealed class TaskTestResultOperation(Func<AegiTaskExecutionContext, Task<int>> execute) : AegiTask<int>
{
    public override string Name => "result";

    protected override Task<int> ExecuteResultAsync(AegiTaskExecutionContext context)
    {
        return execute(context);
    }
}
