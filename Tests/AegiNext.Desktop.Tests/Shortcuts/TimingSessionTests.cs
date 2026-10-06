using AegiNext.Core.Timing;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Tests.Shortcuts;

public sealed class TimingSessionTests
{
    [Fact]
    public void EveryEnterCreatesANewLineAndNeverReusesSelection()
    {
        var selected = Guid.NewGuid();
        var initial = new TimingSession();
        var first = initial.Enter(new(1, 3));
        Assert.NotEqual(selected, first.CueId);
        Assert.True(first.IsNew);
        Assert.False(first.IsRepeated);
        Assert.Equal(new MediaTime(1, 3), first.Start);
        Assert.Null(initial.ActiveCueId);
        Assert.Null(initial.ActiveStart);

        var ended = Assert.IsType<TimingExitResult>(first.Session.Exit(new(2, 3)));
        Assert.Equal(first.CueId, ended.CueId);
        Assert.Equal(new MediaTime(1, 3), ended.Start);
        Assert.Equal(new MediaTime(2, 3), ended.End);
        Assert.Null(ended.Session.ActiveCueId);
        Assert.Null(ended.Session.Exit(new(4, 3)));

        var second = ended.Session.Enter(new(5, 3));
        Assert.True(second.IsNew);
        Assert.NotEqual(Guid.Empty, second.CueId);
        Assert.NotEqual(selected, second.CueId);
        var third = Assert.IsType<TimingExitResult>(second.Session.Exit(new(2))).Session.Enter(new(7, 3));
        Assert.True(third.IsNew);
        Assert.NotEqual(second.CueId, third.CueId);
    }

    [Fact]
    public void RepeatedEnterDoesNotMoveStartOrReplaceActiveLine()
    {
        var first = new TimingSession().Enter(new(4, 3));
        var repeated = first.Session.Enter(new(99));
        Assert.True(first.IsNew);
        Assert.True(repeated.IsRepeated);
        Assert.False(repeated.IsNew);
        Assert.Same(first.Session, repeated.Session);
        Assert.Equal(first.CueId, repeated.CueId);
        Assert.Equal(first.Start, repeated.Start);
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(0, 1)]
    public void NonPositiveDurationCannotCommitOrConsumeTheActiveSession(long numerator, long denominator)
    {
        var entered = new TimingSession().Enter(new(1, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => entered.Session.Exit(new(numerator, denominator)));
        Assert.Equal(entered.CueId, entered.Session.ActiveCueId);
        Assert.Equal(entered.Start, entered.Session.ActiveStart);
        Assert.NotNull(entered.Session.Exit(new(2, 3)));
    }

    [Fact]
    public void ResetAbandonsPendingCycleAndRestartsWithANewLine()
    {
        var active = new TimingSession().Enter(new(1));
        var afterSeek = active.Session.Reset();
        Assert.Null(afterSeek.ActiveCueId);
        Assert.Null(afterSeek.ActiveStart);
        Assert.Null(afterSeek.Exit(new(5)));
        var selected = Guid.NewGuid();
        var restarted = afterSeek.Enter(new(-1, 3));
        Assert.NotEqual(selected, restarted.CueId);
        Assert.True(restarted.IsNew);
        Assert.Equal(new MediaTime(-1, 3), restarted.Start);
        Assert.NotNull(restarted.Session.Exit(MediaTime.Zero));
    }

    [Fact]
    public void RejectedEditorUpdateDoesNotAdvanceOriginalSession()
    {
        var initial = new TimingSession();
        var candidate = initial.Enter(new(1));
        Assert.Null(initial.ActiveCueId);
        var endCandidate = Assert.IsType<TimingExitResult>(candidate.Session.Exit(new(2)));
        Assert.Equal(candidate.CueId, candidate.Session.ActiveCueId);
        Assert.Equal(candidate.Start, candidate.Session.ActiveStart);
        Assert.Null(endCandidate.Session.ActiveCueId);
    }

}
