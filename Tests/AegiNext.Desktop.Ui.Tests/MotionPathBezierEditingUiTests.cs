using AegiNext.Application;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class MotionPathBezierEditingUiTests
{
    [AvaloniaTheory]
    [InlineData(0.2, false)]
    [InlineData(0.8, false)]
    [InlineData(0.2, true)]
    [InlineData(0.8, true)]
    public async Task ShiftClickSplitsTheProjectedCubicAtTheClickedParameterOnReleaseWithOneUndo(double progress, bool transformed)
    {
        var (document, layer) = CreateScene(transformed);
        var originalPath = layer.MotionPath!.Path;
        var editor = new ProjectEditor(document);
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.PATH };
        canvas.SetScene(document, layer, MediaTime.Zero);
        var edits = RecordEdits(canvas, editor);
        var window = Show(canvas);
        try
        {
            await PresentAsync(window, canvas);
            var point = Project(canvas, document, layer, Evaluate(originalPath, 0, progress));
            Assert.Same(canvas, window.InputHitTest(point));
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.Shift);
            Assert.True(canvas.HasActiveDrag);
            Assert.Empty(edits);
            Assert.Same(document, editor.Snapshot);
            window.MouseUp(point, MouseButton.Left, RawInputModifiers.Shift);
            var edit = Assert.Single(edits);
            Assert.Equal(layer.Id, edit.LayerId);
            Assert.False(canvas.HasActiveDrag);
            AssertPath(PathOperations.SplitSegment(originalPath, 0, progress), edit.Path!.Path);
            AssertUntouchedLayerFields(layer, FindLayer(editor.Snapshot, layer.Id));
            AssertOneUndo(editor, document);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ControlClickRemovesOnlyTheHitAnchorOnReleaseWithOneUndo(int anchorIndex)
    {
        var (document, layer) = CreateScene();
        var originalPath = layer.MotionPath!.Path;
        var anchor = anchorIndex == 0 ? originalPath.Start : originalPath.Segments[anchorIndex - 1].End;
        var editor = new ProjectEditor(document);
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.PATH };
        canvas.SetScene(document, layer, MediaTime.Zero);
        var edits = RecordEdits(canvas, editor);
        var window = Show(canvas);
        try
        {
            await PresentAsync(window, canvas);
            var point = Project(canvas, document, layer, anchor);
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.Control);
            Assert.True(canvas.HasActiveDrag);
            Assert.Empty(edits);
            Assert.Same(document, editor.Snapshot);
            window.MouseUp(point, MouseButton.Left, RawInputModifiers.Control);
            var edit = Assert.Single(edits);
            Assert.False(canvas.HasActiveDrag);
            AssertPath(PathOperations.RemovePoint(originalPath, anchorIndex), edit.Path!.Path);
            AssertUntouchedLayerFields(layer, FindLayer(editor.Snapshot, layer.Id));
            AssertOneUndo(editor, document);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ControlTakesPriorityOverShiftAtAnAnchor()
    {
        var (document, layer) = CreateScene();
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.PATH };
        canvas.SetScene(document, layer, MediaTime.Zero);
        var edits = new List<CanvasLayerEditEventArgs>();
        canvas.LayerEdited += (_, e) => edits.Add(e);
        var window = Show(canvas);
        try
        {
            await PresentAsync(window, canvas);
            var point = Project(canvas, document, layer, layer.MotionPath!.Path.Segments[0].End);
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.Empty(edits);
            window.MouseUp(point, MouseButton.Left, RawInputModifiers.Control | RawInputModifiers.Shift);
            AssertPath(PathOperations.RemovePoint(layer.MotionPath.Path, 1), Assert.Single(edits).Path!.Path);
            Assert.False(canvas.HasActiveDrag);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("shift-blank")]
    [InlineData("control-blank")]
    [InlineData("control-handle")]
    [InlineData("shift-anchor")]
    [InlineData("alt-shift")]
    [InlineData("minimum")]
    public async Task ModifiersDoNotEditBlankSpaceHandlesExistingVerticesOrTheMinimumPath(string scenario)
    {
        var (document, layer) = CreateScene(singleSegment: scenario == "minimum");
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.PATH };
        canvas.SetScene(document, layer, MediaTime.Zero);
        var edits = new List<CanvasLayerEditEventArgs>();
        canvas.LayerEdited += (_, e) => edits.Add(e);
        var window = Show(canvas);
        try
        {
            await PresentAsync(window, canvas);
            var path = layer.MotionPath!.Path;
            var local = scenario switch
            {
                "control-handle" => path.Segments[0].Control1,
                "shift-anchor" or "minimum" => path.Start,
                "alt-shift" => Evaluate(path, 0, 0.2),
                _ => new ScenePoint(700, 300)
            };
            var modifiers = scenario switch
            {
                "control-blank" or "control-handle" or "minimum" => RawInputModifiers.Control,
                "alt-shift" => RawInputModifiers.Alt | RawInputModifiers.Shift,
                _ => RawInputModifiers.Shift
            };
            var point = Project(canvas, document, layer, local);
            window.MouseDown(point, MouseButton.Left, modifiers);
            Assert.False(canvas.HasActiveDrag);
            window.MouseUp(point, MouseButton.Left, modifiers);
            Assert.Empty(edits);
            Assert.False(canvas.HasActiveDrag);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("escape", true)]
    [InlineData("capture", true)]
    [InlineData("detach", true)]
    [InlineData("mode", true)]
    [InlineData("document", true)]
    [InlineData("selection", true)]
    [InlineData("layer", true)]
    [InlineData("time", true)]
    [InlineData("release-away", true)]
    [InlineData("escape", false)]
    [InlineData("capture", false)]
    [InlineData("detach", false)]
    [InlineData("mode", false)]
    [InlineData("document", false)]
    [InlineData("selection", false)]
    [InlineData("layer", false)]
    [InlineData("time", false)]
    [InlineData("release-away", false)]
    public async Task CancelledTopologyGestureNeverPublishesALayerEdit(string cancellation, bool inserting)
    {
        var (document, layer) = CreateScene();
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.PATH, Focusable = true };
        canvas.SetScene(document, layer, MediaTime.Zero);
        var edits = new List<CanvasLayerEditEventArgs>();
        var cancels = 0;
        IPointer? pointer = null;
        canvas.LayerEdited += (_, e) => edits.Add(e);
        canvas.GestureCancelled += (_, _) => cancels++;
        canvas.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
            RoutingStrategies.Bubble, handledEventsToo: true);
        var window = Show(canvas);
        try
        {
            await PresentAsync(window, canvas);
            var local = inserting ? Evaluate(layer.MotionPath!.Path, 0, 0.2) : layer.MotionPath!.Path.Segments[0].End;
            var point = Project(canvas, document, layer, local);
            var modifiers = inserting ? RawInputModifiers.Shift : RawInputModifiers.Control;
            Assert.True(canvas.Focus());
            window.MouseDown(point, MouseButton.Left, modifiers);
            Assert.True(canvas.HasActiveDrag);
            Assert.Empty(edits);
            Assert.Same(canvas, Assert.IsAssignableFrom<IPointer>(pointer).Captured);
            switch (cancellation)
            {
                case "escape":
                    UiTestActions.Press(window, Key.Escape);
                    break;
                case "capture":
                    pointer!.Capture(null);
                    break;
                case "detach":
                    window.Content = null;
                    window.Content = canvas;
                    window.UpdateLayout();
                    break;
                case "mode":
                    canvas.EditMode = CanvasEditMode.POSITION;
                    break;
                case "document":
                    var replacement = layer with { Opacity = 0.6 };
                    canvas.SetScene(document with { Layers = [replacement] }, replacement, MediaTime.Zero);
                    break;
                case "selection":
                    canvas.SetScene(document, null, MediaTime.Zero);
                    break;
                case "layer":
                    canvas.SetScene(document, layer with { Name = "Replacement layer object" }, MediaTime.Zero);
                    break;
                case "time":
                    canvas.SetScene(document, layer, new(1));
                    break;
            }
            window.MouseUp(cancellation == "release-away" ? point + new Vector(40, 40) : point, MouseButton.Left, modifiers);
            Assert.Empty(edits);
            Assert.False(canvas.HasActiveDrag);
            Assert.Equal(1, cancels);
            Assert.Null(pointer!.Captured);
        }
        finally
        {
            pointer?.Capture(null);
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SnapshotReplacementDuringGestureStartingCannotReuseTheOldTopology(bool inserting)
    {
        var (document, layer) = CreateScene();
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.PATH };
        canvas.SetScene(document, layer, MediaTime.Zero);
        var edits = new List<CanvasLayerEditEventArgs>();
        canvas.LayerEdited += (_, e) => edits.Add(e);
        canvas.GestureStarting += (_, _) =>
        {
            var replacement = layer with { MotionPath = layer.MotionPath! with { Path = PathOperations.RemovePoint(layer.MotionPath.Path, 1) } };
            canvas.SetScene(document with { Layers = [replacement] }, replacement, MediaTime.Zero);
        };
        var window = Show(canvas);
        try
        {
            await PresentAsync(window, canvas);
            var local = inserting ? Evaluate(layer.MotionPath!.Path, 0, 0.2) : layer.MotionPath!.Path.Segments[0].End;
            var point = Project(canvas, document, layer, local);
            var modifiers = inserting ? RawInputModifiers.Shift : RawInputModifiers.Control;
            window.MouseDown(point, MouseButton.Left, modifiers);
            Assert.False(canvas.HasActiveDrag);
            window.MouseUp(point, MouseButton.Left, modifiers);
            Assert.Empty(edits);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task OrdinaryHandleDragPreservesTheClipTransformAndCommitsOnlyAtRelease()
    {
        var (document, layer) = CreateScene(transformed: true);
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.PATH };
        canvas.SetScene(document, layer, MediaTime.Zero);
        var edits = new List<CanvasLayerEditEventArgs>();
        canvas.LayerEdited += (_, e) => edits.Add(e);
        var window = Show(canvas);
        try
        {
            await PresentAsync(window, canvas);
            var original = layer.MotionPath!.Path.Segments[0].Control1;
            var point = Project(canvas, document, layer, original);
            var destination = Project(canvas, document, layer, new(original.X + 18, original.Y - 13));
            window.MouseDown(point, MouseButton.Left);
            Assert.True(canvas.HasActiveDrag);
            window.MouseMove(destination, RawInputModifiers.LeftMouseButton);
            Assert.Empty(edits);
            window.MouseUp(destination, MouseButton.Left);
            var edit = Assert.Single(edits);
            AssertPath(EffectCanvasControl.MoveHandle(layer.MotionPath.Path, 1, new(18, -13)), edit.Path!.Path);
            Assert.Equal(layer.Transform, edit.Transform);
            Assert.False(canvas.HasActiveDrag);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PreviewPanelTopologyGestureCommitsThroughTheSessionWithOneUndo(bool inserting)
    {
        await using var context = new MainWindowTestContext();
        var (document, layer) = CreateScene();
        context.Session.Editor.Reset(document);
        context.ViewModel.Timeline.SelectLayer(layer.Id);
        context.ViewModel.Effects.EditMode = CanvasEditMode.PATH;
        Dispatcher.UIThread.RunJobs();
        var canvas = UiTestActions.Find<EffectCanvasControl>(context.Window, "EffectCanvas");
        await PresentAsync(context.Window, canvas);
        var local = inserting ? Evaluate(layer.MotionPath!.Path, 0, 0.2) : layer.MotionPath!.Path.Segments[0].End;
        var point = canvas.TranslatePoint(Project(canvas, document, layer, local), context.Window)!.Value;
        Assert.Same(canvas, context.Window.InputHitTest(point));
        var edits = 0;
        canvas.LayerEdited += (_, _) => edits++;
        var modifiers = inserting ? RawInputModifiers.Shift : RawInputModifiers.Control;
        context.Window.MouseDown(point, MouseButton.Left, modifiers);
        Assert.True(canvas.HasActiveDrag);
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        context.Window.MouseUp(point, MouseButton.Left, modifiers);
        Dispatcher.UIThread.RunJobs();
        var changed = FindLayer(context.Session.DocumentSnapshot, layer.Id);
        var expected = inserting ? PathOperations.SplitSegment(layer.MotionPath.Path, 0, 0.2) : PathOperations.RemovePoint(layer.MotionPath.Path, 1);
        AssertPath(expected, changed.MotionPath!.Path);
        Assert.Equal(1, edits);
        AssertUntouchedLayerFields(layer, changed);
        Assert.Null(context.Session.SceneEditing.GestureTarget);
        AssertOneUndo(context.Session.Editor, document);
    }

    [AvaloniaFact]
    public async Task ExistingCanvasCommitPreservesTheSubtitleMaskAndAllUntouchedFields()
    {
        await using var context = new MainWindowTestContext();
        var (_, shape) = CreateScene();
        var cue = new SubtitleLine { Text = string.Empty, End = shape.End };
        var mask = new RectangleClipMask { TopLeft = new(10, 20), BottomRight = new(600, 400) };
        var layer = shape with { Kind = LayerKind.SUBTITLE, Shape = null, SubtitleId = cue.Id, Mask = mask };
        var document = new ProjectDocument { Width = 1000, Height = 600, Subtitles = [cue], Layers = [layer] };
        context.Session.Editor.Reset(document);
        context.Session.SelectCue(cue.Id);
        var editedPath = PathOperations.SplitSegment(layer.MotionPath!.Path, 0, 0.2);
        Assert.True(context.Session.BeginCanvasGesture());
        await context.Session.CommitCanvasAsync(new(layer.Id, layer.Transform, layer.MotionPath with { Path = editedPath }));
        var changed = FindLayer(context.Session.DocumentSnapshot, layer.Id);
        Assert.Same(mask, changed.Mask);
        AssertUntouchedLayerFields(layer, changed);
        AssertPath(editedPath, changed.MotionPath!.Path);
        AssertOneUndo(context.Session.Editor, document);
    }

    [AvaloniaFact]
    public void PathModifierCursorsUpdateWithoutPointerMovementWhileAnotherInputHasFocus()
    {
        var (document, layer) = CreateScene();
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.PATH, Height = 300 };
        canvas.SetScene(document, layer, MediaTime.Zero);
        var input = new TextBox();
        var host = new StackPanel();
        host.Children.Add(input);
        host.Children.Add(canvas);
        var window = new Window { Width = 700, Height = 400, Content = host };
        window.Show();
        try
        {
            window.UpdateLayout();
            Assert.True(input.Focus());
            window.MouseMove(canvas.TranslatePoint(new Point(200, 100), window)!.Value);
            var normal = canvas.Cursor;
            Assert.NotNull(normal);
            window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control, PhysicalKey.ControlLeft, null);
            var deletion = canvas.Cursor;
            Assert.NotNull(deletion);
            Assert.NotSame(normal, deletion);
            window.KeyRelease(Key.LeftCtrl, RawInputModifiers.None, PhysicalKey.ControlLeft, null);
            Assert.Same(normal, canvas.Cursor);
            window.KeyPress(Key.LeftShift, RawInputModifiers.Shift, PhysicalKey.ShiftLeft, null);
            var insertion = canvas.Cursor;
            Assert.NotNull(insertion);
            Assert.NotSame(normal, insertion);
            Assert.NotSame(deletion, insertion);
            window.KeyPress(Key.LeftAlt, RawInputModifiers.Alt | RawInputModifiers.Shift, PhysicalKey.AltLeft, null);
            Assert.Same(normal, canvas.Cursor);
            window.KeyRelease(Key.LeftAlt, RawInputModifiers.Shift, PhysicalKey.AltLeft, null);
            Assert.Same(insertion, canvas.Cursor);
            window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.ControlLeft, null);
            Assert.Same(deletion, canvas.Cursor);
            window.KeyRelease(Key.LeftCtrl, RawInputModifiers.Shift, PhysicalKey.ControlLeft, null);
            Assert.Same(insertion, canvas.Cursor);
            window.KeyRelease(Key.LeftShift, RawInputModifiers.None, PhysicalKey.ShiftLeft, null);
            Assert.Same(normal, canvas.Cursor);
            Assert.Same(input, TopLevel.GetTopLevel(input)!.FocusManager!.GetFocusedElement());
            host.Children.Remove(canvas);
            host.Children.Add(canvas);
            window.UpdateLayout();
            Assert.True(input.Focus());
            window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control, PhysicalKey.ControlLeft, null);
            Assert.Same(deletion, canvas.Cursor);
            window.KeyRelease(Key.LeftCtrl, RawInputModifiers.None, PhysicalKey.ControlLeft, null);
            canvas.EditMode = CanvasEditMode.POSITION;
            Assert.Null(canvas.Cursor);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RejectedGestureStartingNeverCapturesOrPublishesATopologyEdit(bool inserting)
    {
        var (document, layer) = CreateScene();
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.PATH };
        canvas.SetScene(document, layer, MediaTime.Zero);
        var starts = 0;
        var edits = new List<CanvasLayerEditEventArgs>();
        IPointer? pointer = null;
        canvas.GestureStarting += (_, e) =>
        {
            starts++;
            e.Cancel = true;
        };
        canvas.LayerEdited += (_, e) => edits.Add(e);
        canvas.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
            RoutingStrategies.Bubble, handledEventsToo: true);
        var window = Show(canvas);
        try
        {
            await PresentAsync(window, canvas);
            var local = inserting ? Evaluate(layer.MotionPath!.Path, 0, 0.2) : layer.MotionPath!.Path.Segments[0].End;
            var point = Project(canvas, document, layer, local);
            var modifiers = inserting ? RawInputModifiers.Shift : RawInputModifiers.Control;
            window.MouseDown(point, MouseButton.Left, modifiers);
            Assert.Equal(1, starts);
            Assert.False(canvas.HasActiveDrag);
            Assert.IsAssignableFrom<IPointer>(pointer);
            window.MouseUp(point, MouseButton.Left, modifiers);
            Assert.Empty(edits);
            Assert.False(canvas.HasActiveDrag);
            Assert.Null(pointer!.Captured);
        }
        finally
        {
            pointer?.Capture(null);
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SceneReplacementDuringCaptureReleaseCannotPublishThePreviousTopologyEdit(bool inserting)
    {
        var (document, layer) = CreateScene();
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.PATH };
        canvas.SetScene(document, layer, MediaTime.Zero);
        var replacement = layer with
        {
            MotionPath = layer.MotionPath! with { Path = PathOperations.AppendPoint(layer.MotionPath.Path, new(440, 40)) }
        };
        var replacementDocument = document with { Layers = [replacement] };
        var edits = new List<CanvasLayerEditEventArgs>();
        var captureLosses = 0;
        canvas.LayerEdited += (_, e) => edits.Add(e);
        canvas.PointerCaptureLost += (_, _) =>
        {
            captureLosses++;
            canvas.SetScene(replacementDocument, replacement, MediaTime.Zero);
        };
        var window = Show(canvas);
        try
        {
            await PresentAsync(window, canvas);
            var local = inserting ? Evaluate(layer.MotionPath!.Path, 0, 0.2) : layer.MotionPath!.Path.Segments[0].End;
            var point = Project(canvas, document, layer, local);
            var modifiers = inserting ? RawInputModifiers.Shift : RawInputModifiers.Control;
            window.MouseDown(point, MouseButton.Left, modifiers);
            Assert.True(canvas.HasActiveDrag);
            window.MouseUp(point, MouseButton.Left, modifiers);
            Assert.Equal(1, captureLosses);
            Assert.Empty(edits);
            Assert.False(canvas.HasActiveDrag);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EffectiveEndpointPoseChangeDuringGestureStartingCannotActivateThePreviousTopology(bool inserting)
    {
        var (document, layer) = CreateScene();
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.PATH };
        canvas.SetScene(document, layer, layer.End, editorPose: true);
        var starts = 0;
        var edits = new List<CanvasLayerEditEventArgs>();
        IPointer? pointer = null;
        canvas.LayerEdited += (_, e) => edits.Add(e);
        canvas.GestureStarting += (_, _) =>
        {
            starts++;
            canvas.SetScene(document, layer, layer.End, editorPose: false);
        };
        canvas.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
            RoutingStrategies.Bubble, handledEventsToo: true);
        var window = Show(canvas);
        try
        {
            await PresentAsync(window, canvas);
            var local = inserting ? Evaluate(layer.MotionPath!.Path, 0, 0.2) : layer.MotionPath!.Path.Segments[0].End;
            var point = Project(canvas, document, layer, local);
            var modifiers = inserting ? RawInputModifiers.Shift : RawInputModifiers.Control;
            window.MouseDown(point, MouseButton.Left, modifiers);
            Assert.Equal(1, starts);
            Assert.False(canvas.HasActiveDrag);
            Assert.IsAssignableFrom<IPointer>(pointer);
            window.MouseUp(point, MouseButton.Left, modifiers);
            Assert.Empty(edits);
            Assert.Null(pointer!.Captured);
        }
        finally
        {
            pointer?.Capture(null);
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DetachmentDuringFocusCannotActivateOrCaptureThePendingTopology(bool inserting)
    {
        var (document, layer) = CreateScene();
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.PATH, Height = 360 };
        canvas.SetScene(document, layer, MediaTime.Zero);
        var input = new TextBox();
        var host = new StackPanel();
        host.Children.Add(input);
        host.Children.Add(canvas);
        var window = new Window { Width = 700, Height = 440, Content = host };
        var edits = new List<CanvasLayerEditEventArgs>();
        var focusRequests = 0;
        IPointer? pointer = null;
        canvas.LayerEdited += (_, e) => edits.Add(e);
        canvas.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
            RoutingStrategies.Bubble, handledEventsToo: true);
        window.Show();
        try
        {
            await PresentAsync(window, canvas);
            Assert.True(input.Focus());
            canvas.GotFocus += (_, _) =>
            {
                focusRequests++;
                window.Content = null;
            };
            var local = inserting ? Evaluate(layer.MotionPath!.Path, 0, 0.2) : layer.MotionPath!.Path.Segments[0].End;
            var point = canvas.TranslatePoint(Project(canvas, document, layer, local), window)!.Value;
            var modifiers = inserting ? RawInputModifiers.Shift : RawInputModifiers.Control;
            window.MouseDown(point, MouseButton.Left, modifiers);
            Assert.Equal(1, focusRequests);
            Assert.Null(window.Content);
            Assert.False(canvas.HasActiveDrag);
            Assert.IsAssignableFrom<IPointer>(pointer);
            window.MouseUp(point, MouseButton.Left, modifiers);
            Assert.Empty(edits);
            Assert.Null(pointer!.Captured);
        }
        finally
        {
            pointer?.Capture(null);
            window.Close();
        }
    }

    private static (ProjectDocument Document, ProjectLayer Layer) CreateScene(bool transformed = false, bool singleSegment = false)
    {
        var path = new PathGeometry(new(0, 0),
            [new(new(40, -100), new(120, 100), new(160, 0)), new(new(200, -80), new(280, 80), new(320, 0))]);
        if (singleSegment)
        {
            path = path with { Segments = [path.Segments[0]] };
        }
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20),
            Transform = transformed ? new(250, 180, ScaleX: 1.25, ScaleY: 0.85, Rotation: 25) : new(100, 180),
            Fill = new(0.2, 0.7, 0.4), StrokeWidth = 2, Blur = 0.5, Blend = BlendMode.SCREEN,
            MotionPath = new(path, new(7), true),
            Tracks = [new(AnimationProperty.OPACITY, [new(new(0), 0.7), new(new(5), 0.9)]),
                new(AnimationProperty.PATH_PROGRESS, [new(new(0), 0), new(new(5), 0.8)])]
        };
        return (new() { Width = 1000, Height = 600, Layers = [layer] }, layer);
    }

    private static Window Show(EffectCanvasControl canvas)
    {
        var window = new Window { Width = 700, Height = 440, Content = canvas };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static List<CanvasLayerEditEventArgs> RecordEdits(EffectCanvasControl canvas, ProjectEditor editor)
    {
        var edits = new List<CanvasLayerEditEventArgs>();
        canvas.LayerEdited += (_, e) =>
        {
            edits.Add(e);
            editor.UpdateLayer(e.LayerId, value => value with { Transform = e.Transform, MotionPath = e.Path });
        };
        return edits;
    }

    private static ScenePoint Evaluate(PathGeometry path, int segmentIndex, double progress) =>
        SceneEvaluator.EvaluatePath(path, (segmentIndex + progress) / path.Segments.Length);

    private static Point Project(EffectCanvasControl canvas, ProjectDocument document, ProjectLayer layer, ScenePoint value)
    {
        var matrix = Matrix.CreateTranslation(layer.Transform.X, layer.Transform.Y);
        var board = canvas.ProjectRectangle;
        matrix *= Matrix.CreateScale(board.Width / document.Width, board.Height / document.Height) * Matrix.CreateTranslation(board.X, board.Y);
        return new Point(value.X, value.Y) * matrix;
    }

    private static ProjectLayer FindLayer(ProjectDocument document, Guid id) =>
        document.Layers.Single(layer => layer.Id == id);

    private static void AssertUntouchedLayerFields(ProjectLayer original, ProjectLayer changed)
    {
        Assert.Equal(original with { MotionPath = changed.MotionPath }, changed);
        Assert.Equal(original.MotionPath!.Duration, changed.MotionPath!.Duration);
        Assert.Equal(original.MotionPath.OrientToPath, changed.MotionPath.OrientToPath);
        Assert.Equal(original.Tracks, changed.Tracks);
        Assert.Same(original.Mask, changed.Mask);
    }

    private static void AssertPath(PathGeometry expected, PathGeometry actual)
    {
        Assert.Equal(expected.Closed, actual.Closed);
        AssertPoint(expected.Start, actual.Start);
        Assert.Equal(expected.Segments.Length, actual.Segments.Length);
        for (var index = 0; index < expected.Segments.Length; index++)
        {
            AssertPoint(expected.Segments[index].Control1, actual.Segments[index].Control1);
            AssertPoint(expected.Segments[index].Control2, actual.Segments[index].Control2);
            AssertPoint(expected.Segments[index].End, actual.Segments[index].End);
        }
    }

    private static void AssertPoint(ScenePoint expected, ScenePoint actual)
    {
        Assert.InRange(Math.Abs(expected.X - actual.X), 0, 0.0001);
        Assert.InRange(Math.Abs(expected.Y - actual.Y), 0, 0.0001);
    }

    private static void AssertOneUndo(ProjectEditor editor, ProjectDocument original)
    {
        var changed = editor.Snapshot;
        Assert.True(editor.Undo());
        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        Assert.Same(changed, editor.Snapshot);
    }

    private static async Task PresentAsync(Window window, EffectCanvasControl canvas)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        do
        {
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            await canvas.PreviewCompletion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            if (canvas.PresentedPreviewSequence == canvas.PreviewSequence)
            {
                return;
            }
            await Task.Yield();
        } while (DateTime.UtcNow < deadline);
        Assert.Fail("The shape preview did not present before the motion path gesture.");
    }
}
