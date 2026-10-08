using AegiNext.Application.Tasks;

namespace AegiNext.Application.Tests.Tasks;

public sealed class AegiTaskStorageSchedulingTests
{
    [Fact]
    public async Task DanglingStorageAliasSerializesWritesAcrossIndependentProjectScopes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "aegi-task-storage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var target = Path.Combine(directory, "pending.json");
            var alias = Path.Combine(directory, "alias.json");
            File.CreateSymbolicLink(alias, target);
            Assert.False(File.Exists(target));
            await using var service = new AegiTaskService();
            service.RegisterScope("first", "First project");
            service.RegisterScope("second", "Second project");
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var first = service.Submit(new TaskTestOperation(scopeId: "first",
                resources: [AegiTaskResource.Project("first"), AegiTaskResource.StoragePath(target)])
            {
                Execute = async _ =>
                {
                    entered.TrySetResult();
                    await release.Task;
                }
            });
            AegiTaskHandle? second = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                second = service.Submit(new TaskTestOperation(scopeId: "second",
                    resources: [AegiTaskResource.Project("second"), AegiTaskResource.StoragePath(alias)])
                {
                    Execute = _ =>
                    {
                        secondEntered.TrySetResult();
                        return Task.CompletedTask;
                    }
                });

                Assert.Equal(AegiTaskState.Queued, second.Snapshot.State);
                Assert.False(secondEntered.Task.IsCompleted);
            }
            finally
            {
                release.TrySetResult();
            }

            await Task.WhenAll(first.Completion, Assert.IsType<AegiTaskHandle>(second).Completion)
                .WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(secondEntered.Task.IsCompleted);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
