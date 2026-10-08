using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

public sealed class WorkbenchUpdateStateTests
{
    [Fact]
    public void InterleavedRefreshesRemainProtectedUntilTheLastLeaseFinishes()
    {
        var state = new WorkbenchUpdateState();
        using var first = state.Acquire();
        using var second = state.Acquire();
        first.Dispose();
        Assert.True(state.IsActive);
        second.Dispose();
        Assert.False(state.IsActive);
        first.Dispose();
        using var next = state.Acquire();
        Assert.True(state.IsActive);
        next.Dispose();
        Assert.False(state.IsActive);
    }

    [Fact]
    public async Task ConcurrentRefreshesDoNotRestoreAStaleBusyState()
    {
        var state = new WorkbenchUpdateState();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Task.Run(async () =>
        {
            using (state.Acquire())
            {
                firstEntered.TrySetResult();
                await secondEntered.Task;
            }
            firstExited.TrySetResult();
        });
        var second = Task.Run(async () =>
        {
            await firstEntered.Task;
            using (state.Acquire())
            {
                secondEntered.TrySetResult();
                await firstExited.Task;
                Assert.True(state.IsActive);
            }
        });
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(state.IsActive);
    }
}
