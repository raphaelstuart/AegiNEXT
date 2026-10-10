using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

internal sealed class KaraokeAxisUiTestHost : IDisposable
{
    private readonly UiTestEnvironment environment = new();
    internal KaraokeClipAxis Axis { get; } = new() { IsSnapEnabled = false };
    internal Window Window { get; }
    internal SubtitleLine Line { get; private set; }
    internal MediaTime Offset { get; }
    internal List<KaraokeClipRangeEventArgs> Requests { get; } = [];
    internal List<KaraokeClipEditRequestedEventArgs> EditRequests { get; } = [];
    internal IPointer? Pointer { get; private set; }

    internal KaraokeAxisUiTestHost(SubtitleLine line, MediaTime offset = default)
    {
        Line = line;
        Offset = offset;
        Axis.SetContent(line, offset, line.Karaoke.FirstOrDefault()?.Id);
        Axis.SelectionRequested += (_, e) => Axis.SetContent(Line, Offset, e.PrimaryClipId, e.SelectedClipIds);
        Axis.RangeRequested += (_, e) => Requests.Add(e);
        Axis.ClipEditRequested += (_, e) => EditRequests.Add(e);
        Axis.AddHandler(InputElement.PointerPressedEvent, (_, e) => Pointer = e.Pointer, RoutingStrategies.Bubble, true);
        Window = new() { Width = 424, Height = Math.Max(180, Axis.Height + 80), Content = Axis };
        Window.Show();
        Flush();
    }

    internal Point Point(Point point) => Axis.TranslatePoint(point, Window)!.Value;
    internal void Flush()
    {
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
    }
    internal void Replace(SubtitleLine line, Guid? selection)
    {
        Line = line;
        Axis.SetContent(line, Offset, selection);
        Flush();
    }
    internal void Drag(Point start, Vector delta, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var point = Point(start);
        Window.MouseDown(point, MouseButton.Left, modifiers);
        Window.MouseMove(point + delta, modifiers);
        Window.MouseUp(point + delta, MouseButton.Left, modifiers);
        Flush();
    }
    public void Dispose()
    {
        Window.Close();
        environment.Dispose();
    }
}
