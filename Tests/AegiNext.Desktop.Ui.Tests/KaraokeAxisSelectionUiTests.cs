using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class KaraokeAxisSelectionUiTests
{
    [AvaloniaTheory]
    [InlineData(RawInputModifiers.Control)]
    [InlineData(RawInputModifiers.Meta)]
    public void ToggleSelectionKeepsTextOrderAndDoesNotOpenOrEditTheClip(RawInputModifiers modifier)
    {
        using var host = new KaraokeAxisUiTestHost(Line());
        Click(host, 2, modifier);
        Assert.Equal(new[] { host.Line.Karaoke[0].Id, host.Line.Karaoke[2].Id }, host.Axis.SelectedClipIds);
        Click(host, 0, modifier);
        Assert.Equal(new[] { host.Line.Karaoke[2].Id }, host.Axis.SelectedClipIds);
        Click(host, 2, modifier);
        Assert.Empty(host.Axis.SelectedClipIds);
        Assert.Empty(host.EditRequests);
        Assert.Empty(host.Requests);
        Assert.False(host.Axis.HasActiveGesture);
    }

    [AvaloniaFact]
    public void ShiftSelectionUsesTextOrderDespiteReverseTimingAndRetainsTheOriginalAnchor()
    {
        using var host = new KaraokeAxisUiTestHost(Line());
        Click(host, 2, RawInputModifiers.Shift);
        Assert.Equal(host.Line.Karaoke.Take(3).Select(clip => clip.Id), host.Axis.SelectedClipIds);
        Click(host, 1, RawInputModifiers.Shift);
        Assert.Equal(host.Line.Karaoke.Take(2).Select(clip => clip.Id), host.Axis.SelectedClipIds);
        Assert.Empty(host.EditRequests);
        Assert.Empty(host.Requests);
    }

    [AvaloniaFact]
    public void DraggingASelectedMemberKeepsTheSelectionAndChangesOnlyThatMember()
    {
        using var host = new KaraokeAxisUiTestHost(Line());
        Click(host, 1, RawInputModifiers.Control);
        var ids = host.Axis.SelectedClipIds.ToArray();
        var clip = host.Line.Karaoke[0];
        var other = host.Axis.GeometryFor(host.Line.Karaoke[1].Id);
        host.Drag(host.Axis.GeometryFor(clip.Id).Body.Center, new(20, 0));
        Assert.Equal(ids, host.Axis.SelectedClipIds);
        var request = Assert.Single(host.Requests);
        Assert.Equal(clip.Id, request.ClipId);
        Assert.Equal(clip.End - clip.Start, request.End - request.Start);
        Assert.Equal(other, host.Axis.GeometryFor(host.Line.Karaoke[1].Id));
        Assert.Empty(host.EditRequests);
    }

    [AvaloniaFact]
    public void ClickingSelectedMemberOpensItsPopupWithoutClearingTheMergeSelection()
    {
        using var host = new KaraokeAxisUiTestHost(Line());
        Click(host, 1, RawInputModifiers.Meta);
        Click(host, 0);
        Assert.Equal(host.Line.Karaoke.Take(2).Select(clip => clip.Id), host.Axis.SelectedClipIds);
        Assert.Equal(host.Line.Karaoke[0].Id, Assert.Single(host.EditRequests).ClipId);
        Click(host, 3);
        Assert.Equal(new[] { host.Line.Karaoke[3].Id }, host.Axis.SelectedClipIds);
    }

    [AvaloniaFact]
    public void ExternalSelectionSetChangeCancelsEvenWhenPrimaryDoesNotChange()
    {
        using var host = new KaraokeAxisUiTestHost(Line());
        var clip = host.Line.Karaoke[0];
        var point = host.Point(host.Axis.GeometryFor(clip.Id).EndHandle.Center);
        host.Window.MouseDown(point, MouseButton.Left);
        host.Window.MouseMove(point + new Vector(20, 0));
        host.Axis.SetContent(host.Line, host.Offset, clip.Id, [clip.Id, host.Line.Karaoke[1].Id]);
        Assert.False(host.Axis.HasActiveGesture);
        Assert.Null(host.Pointer!.Captured);
        host.Window.MouseUp(point + new Vector(20, 0), MouseButton.Left);
        Assert.Empty(host.Requests);
        Assert.Empty(host.EditRequests);
    }

    [AvaloniaFact]
    public void ClickingEmptyTrackClearsSelectionWithoutOpeningPropertiesOrEditing()
    {
        using var host = new KaraokeAxisUiTestHost(Line());
        Click(host, 1, RawInputModifiers.Control);
        var point = host.Point(new(host.Axis.Bounds.Width - 20, 40));
        host.Window.MouseDown(point, MouseButton.Left);
        host.Window.MouseUp(point, MouseButton.Left);
        host.Flush();
        Assert.Empty(host.Axis.SelectedClipIds);
        Assert.Empty(host.EditRequests);
        Assert.Empty(host.Requests);
    }

    private static void Click(KaraokeAxisUiTestHost host, int index, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var point = host.Point(host.Axis.GeometryFor(host.Line.Karaoke[index].Id).Body.Center);
        host.Window.MouseDown(point, MouseButton.Left, modifiers);
        host.Window.MouseUp(point, MouseButton.Left, modifiers);
        host.Flush();
    }

    private static SubtitleLine Line() => new()
    {
        Text = "abcd", End = new(6), Karaoke =
        [Clip(0, 3, 4), Clip(1, 2, 3), Clip(2, 1, 2), Clip(3, 0, 1)]
    };
    private static KaraokeSegment Clip(int text, long start, long end) => new(text, 1, new(start), new(end), SceneColor.White);
}
