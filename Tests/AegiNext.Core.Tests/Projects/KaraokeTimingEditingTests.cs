using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Projects;

public sealed class KaraokeTimingEditingTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 2)]
    [InlineData(2, 2)]
    [InlineData(1, 2)]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(-1, -1)]
    [InlineData(-1, -2)]
    public void LinkedRangePreservesExactDurationsAndSignedGapsOnBothSides(int startSevenths, int endSevenths)
    {
        var clips = Clips();
        var target = clips[1];
        var startChange = new MediaTime(startSevenths, 7);
        var endChange = new MediaTime(endSevenths, 7);
        var edited = KaraokeTimingEditing.SetRange(clips, target.Id, target.Start + startChange,
            target.End + endChange, true);

        Assert.Equal(clips[0] with { Start = clips[0].Start + startChange, End = clips[0].End + startChange }, edited[0]);
        Assert.Equal(target with { Start = target.Start + startChange, End = target.End + endChange }, edited[1]);
        Assert.Equal(clips[2] with { Start = clips[2].Start + endChange, End = clips[2].End + endChange }, edited[2]);
        Assert.Equal(target.Start - clips[0].End, edited[1].Start - edited[0].End);
        Assert.Equal(clips[2].Start - target.End, edited[2].Start - edited[1].End);
        Assert.Equal(clips[0].End - clips[0].Start, edited[0].End - edited[0].Start);
        Assert.Equal(clips[2].End - clips[2].Start, edited[2].End - edited[2].Start);
        Assert.Equal(new MediaTime(1, 7), clips[0].Start);
        Assert.Equal(new MediaTime(3, 7), clips[1].Start);
        Assert.Equal(new MediaTime(14, 7), clips[2].Start);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentRangeChangesOnlyTheTargetAndAllowsTimeOverlap(bool explicitMode)
    {
        var clips = Clips();
        var edited = explicitMode
            ? KaraokeTimingEditing.SetRange(clips, clips[1].Id, MediaTime.Zero, new(3), false)
            : KaraokeTimingEditing.SetRange(clips, clips[1].Id, MediaTime.Zero, new(3));

        Assert.Same(clips[0], edited[0]);
        Assert.Same(clips[2], edited[2]);
        Assert.Equal(clips[1] with { Start = MediaTime.Zero, End = new(3) }, edited[1]);
    }

    [Fact]
    public void LinkedRangeUsesTextPositionWithoutSortingOrRemovingExistingOverlap()
    {
        ImmutableArray<KaraokeSegment> clips =
        [new(4, 1, new(1), new(2), SceneColor.Black),
            new(0, 2, new(3), new(6), SceneColor.White),
            new(2, 2, new(4), new(5), new(4, -1, 2, 0.5)) { HighlightKind = KaraokeHighlightKind.STEP }];
        var target = clips[2];
        var edited = KaraokeTimingEditing.SetRange(clips, target.Id, new(5), new(7), true);

        Assert.Equal(clips.Select(clip => clip.Id), edited.Select(clip => clip.Id));
        Assert.Equal(clips[0] with { Start = new(3), End = new(4) }, edited[0]);
        Assert.Equal(clips[1] with { Start = new(4), End = new(7) }, edited[1]);
        Assert.Equal(target with { Start = new(5), End = new(7) }, edited[2]);
        Assert.Equal(new MediaTime(-2), edited[2].Start - edited[1].End);
        Assert.Equal(new MediaTime(-4), edited[0].Start - edited[2].End);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnchangedRangeReturnsTheOriginalArrayAndInstances(bool linked)
    {
        var clips = Clips();
        var target = clips[1];
        var edited = KaraokeTimingEditing.SetRange(clips, target.Id, target.Start, target.End, linked);

        Assert.True(clips == edited);
        Assert.All(Enumerable.Range(0, clips.Length), index => Assert.Same(clips[index], edited[index]));
    }

    [Fact]
    public void UnmovedSideRetainsItsOriginalClipInstance()
    {
        var clips = Clips();
        var edited = KaraokeTimingEditing.SetRange(clips, clips[1].Id, clips[1].Start, new(2), true);

        Assert.Same(clips[0], edited[0]);
        Assert.NotSame(clips[2], edited[2]);
    }

    [Fact]
    public void SingleGroupCanMoveAndExtendBeyondItsPreviousEnd()
    {
        ImmutableArray<KaraokeSegment> clips = [new(0, 3, new(1, 3), new(2, 3), SceneColor.White)];
        var edited = KaraokeTimingEditing.SetRange(clips, clips[0].Id, new(2), new(10), true);

        Assert.Equal(clips[0] with { Start = new(2), End = new(10) }, Assert.Single(edited));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    public void InvalidTargetRangeIsRejectedWithoutChangingTheSource(int start, int end)
    {
        var clips = Clips();
        var baseline = clips.ToArray();
        Assert.Throws<ArgumentOutOfRangeException>(() => KaraokeTimingEditing.SetRange(clips,
            clips[1].Id, new(start), new(end), true));
        Assert.Equal(baseline, clips);
    }

    [Theory]
    [InlineData("before")]
    [InlineData("after")]
    public void AnyLinkedGroupCrossingTheContentOriginRejectsTheWholeEdit(string side)
    {
        ImmutableArray<KaraokeSegment> clips =
        [new(0, 1, new(1, 7), new(2, 7), SceneColor.White),
            new(1, 1, new(3), new(5), SceneColor.White),
            new(2, 1, new(1, 7), new(3, 7), SceneColor.White)];
        var baseline = clips.ToArray();
        var start = side == "before" ? new MediaTime(2) : clips[1].Start;
        var end = side == "after" ? new MediaTime(4) : clips[1].End;

        Assert.Throws<ArgumentOutOfRangeException>(() => KaraokeTimingEditing.SetRange(clips,
            clips[1].Id, start, end, true));
        Assert.Equal(baseline, clips);
    }

    [Theory]
    [InlineData("before")]
    [InlineData("after")]
    public void LinkedArithmeticOverflowRejectsTheWholeEdit(string side)
    {
        ImmutableArray<KaraokeSegment> clips =
        [new(0, 1, new(1), new(long.MaxValue), SceneColor.White),
            new(1, 1, new(1), new(2), SceneColor.White),
            new(2, 1, new(1), new(long.MaxValue), SceneColor.White)];
        var baseline = clips.ToArray();
        var start = side == "before" ? new MediaTime(2) : clips[1].Start;
        var end = new MediaTime(3);

        Assert.Throws<OverflowException>(() => KaraokeTimingEditing.SetRange(clips, clips[1].Id, start, end, true));
        Assert.Equal(baseline, clips);
    }

    [Fact]
    public void MissingTargetAndUninitializedSourceAreRejected()
    {
        var clips = Clips();
        Assert.Throws<KeyNotFoundException>(() => KaraokeTimingEditing.SetRange(clips, Guid.NewGuid(), new(1), new(2), true));
        Assert.Throws<KeyNotFoundException>(() => KaraokeTimingEditing.SetRange([], clips[1].Id, new(1), new(2), true));
        Assert.Throws<ArgumentException>(() => KaraokeTimingEditing.SetRange(default, clips[1].Id, new(1), new(2), true));
    }

    private static ImmutableArray<KaraokeSegment> Clips()
    {
        return [new(0, 1, new(1, 7), new(2, 7), new(3, -2, 1, 0.5)) { HighlightKind = KaraokeHighlightKind.STEP },
            new(1, 2, new(3, 7), new(10, 7), SceneColor.White),
            new(3, 1, new(14, 7), new(21, 7), SceneColor.Black) { HighlightKind = KaraokeHighlightKind.OUTLINE_STEP }];
    }
}
