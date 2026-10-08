using AegiNext.Application.Tasks;
using AegiNext.Desktop.Tasks;

namespace AegiNext.Desktop.Tests;

public sealed class TaskRowViewModelTests
{
    [Fact]
    public void MeasuredProgressAndCancelTransitionsKeepTaskIdentityAndCloseTheCancelEntryAtCommit()
    {
        var snapshot = new AegiTaskSnapshot(Guid.NewGuid(), 1, "Scan", "project", "Fixture",
            AegiTaskMode.Parallel, AegiTaskState.Running, true, new(), DateTimeOffset.UtcNow);
        var cancellations = new List<Guid>();
        var row = new TaskRowViewModel(snapshot, cancellations.Add);
        Assert.True(row.IsIndeterminate);
        Assert.True(row.ShowsCancel);
        row.CancelCommand.Execute(null);
        Assert.Equal(snapshot.Id, Assert.Single(cancellations));

        row.Update(snapshot with { Progress = new("Calculation", 3, 4) });
        Assert.False(row.IsIndeterminate);
        Assert.Equal(75, row.ProgressValue);
        Assert.Equal("Calculation", row.Stage);
        Assert.Equal("Calculation", row.SecondaryTitle);
        Assert.Equal(snapshot.Id, row.Id);
        row.Update(snapshot with { State = AegiTaskState.Cancelling, CanCancel = false });
        Assert.True(row.ShowsCancel);
        Assert.False(row.CancelCommand.CanExecute(null));
        row.Update(snapshot with { State = AegiTaskState.Committing, CanCancel = false });
        Assert.False(row.ShowsCancel);
        Assert.False(row.CancelCommand.CanExecute(null));
        row.Update(snapshot with { State = AegiTaskState.Failed, CanCancel = false, ErrorSummary = "Failed to scan" });
        Assert.False(row.IsIndeterminate);
        Assert.True(row.HasError);
        Assert.Equal("Failed to scan", row.Error);
        Assert.Contains("Failed to scan", row.ProgressDescription, StringComparison.Ordinal);
    }

    [Fact]
    public void ARowRejectsAnotherTaskIdentity()
    {
        var snapshot = new AegiTaskSnapshot(Guid.NewGuid(), 1, "Scan", null, null,
            AegiTaskMode.Parallel, AegiTaskState.Queued, true, new(), DateTimeOffset.UtcNow);
        var row = new TaskRowViewModel(snapshot, _ => { });
        Assert.Throws<ArgumentException>(() => row.Update(snapshot with { Id = Guid.NewGuid() }));
    }

    [Theory]
    [InlineData(AegiTaskState.Queued)]
    [InlineData(AegiTaskState.Cancelling)]
    [InlineData(AegiTaskState.Committing)]
    [InlineData(AegiTaskState.Succeeded)]
    [InlineData(AegiTaskState.Cancelled)]
    [InlineData(AegiTaskState.Failed)]
    public void NonRunningStatesReplaceThePreviousStageWithTheirStatus(AegiTaskState state)
    {
        var snapshot = new AegiTaskSnapshot(Guid.NewGuid(), 1, "Scan", "project", "Fixture",
            AegiTaskMode.Parallel, state, false, new("Calculation"), DateTimeOffset.UtcNow);
        var row = new TaskRowViewModel(snapshot, _ => { });
        Assert.Equal(row.Status, row.SecondaryTitle);
        Assert.DoesNotContain("Fixture", row.Description, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Scan")]
    public void ARunningRowUsesStatusWhenItsStageIsMissingOrRepeatsTheName(string? stage)
    {
        var snapshot = new AegiTaskSnapshot(Guid.NewGuid(), 1, "Scan", null, null,
            AegiTaskMode.Parallel, AegiTaskState.Running, true, new(stage), DateTimeOffset.UtcNow);
        var row = new TaskRowViewModel(snapshot, _ => { });
        Assert.Equal(row.Status, row.SecondaryTitle);
    }

    [Theory]
    [InlineData(AegiTaskState.Succeeded, 100)]
    [InlineData(AegiTaskState.Cancelled, 75)]
    [InlineData(AegiTaskState.Failed, 75)]
    public void FinishedProgressIsStaticAndRetainsTheLastKnownAmount(AegiTaskState state, double expected)
    {
        var snapshot = new AegiTaskSnapshot(Guid.NewGuid(), 1, "Scan", null, null,
            AegiTaskMode.Parallel, AegiTaskState.Running, true, new("Calculation", 3, 4), DateTimeOffset.UtcNow);
        var row = new TaskRowViewModel(snapshot, _ => { });
        row.Update(snapshot with { Progress = new("Cleanup") });
        Assert.True(row.IsIndeterminate);
        row.Update(snapshot with { State = state, CanCancel = false, Progress = new("Cleanup") });
        Assert.False(row.IsIndeterminate);
        Assert.Equal(expected, row.ProgressValue);
    }

    [Theory]
    [InlineData(AegiTaskState.Cancelled)]
    [InlineData(AegiTaskState.Failed)]
    public void FinishedUnknownProgressHasAnEmptyStaticBar(AegiTaskState state)
    {
        var snapshot = new AegiTaskSnapshot(Guid.NewGuid(), 1, "Scan", null, null,
            AegiTaskMode.Parallel, state, false, new(), DateTimeOffset.UtcNow);
        var row = new TaskRowViewModel(snapshot, _ => { });
        Assert.False(row.IsIndeterminate);
        Assert.Equal(0, row.ProgressValue);
    }
}
