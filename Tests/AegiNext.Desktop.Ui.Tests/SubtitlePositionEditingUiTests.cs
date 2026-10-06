using System.Globalization;
using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitlePositionEditingUiTests
{
    [AvaloniaFact]
    public async Task RestoreAutomaticPositionReturnsCenteredHorizontalOffsetToExactZero()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        PrepareActualSubtitle(context);
        var window = context.Window;
        var subtitle = Assert.Single(window.DocumentSnapshot.Subtitles);
        context.Session.Editor.UpdateSubtitle(subtitle.Id, line => line with
        {
            Text = "Ajg", Style = line.Style with
            {
                Alignment = AegiNext.Core.Projects.TextAlignment.BOTTOM_CENTER,
                Position = new() { Offset = new(70, -25) }
            }
        });
        context.Session.Editor.UpdateLayer(subtitle.Id, layer => layer with
        {
            Transform = layer.Transform with { Position = new(20, 10) }
        });
        var changed = window.DocumentSnapshot;
        window.Layouts.Activate(WorkbenchPanelIds.STYLES);
        Dispatcher.UIThread.RunJobs();

        await window.ViewModel.Styles.RestoreAutomaticPositionAsync();
        Dispatcher.UIThread.RunJobs();

        Assert.Null(Assert.Single(window.DocumentSnapshot.Subtitles).Style.Position);
        Assert.Equal(default, Assert.Single(window.DocumentSnapshot.Layers).Transform.Position);
        Assert.Equal(0, window.ViewModel.Styles.Position.OffsetX.Parse());
        Assert.False(window.ViewModel.Styles.Position.IsExplicit);
        Assert.Equal(window.DocumentSnapshot.Width / 2m, window.ViewModel.Effects.PositionX);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(changed, window.DocumentSnapshot);
    }

    [AvaloniaFact]
    public async Task MissingEmbeddedFontKeepsTheLoadedProjectEditableAndRepairRestoresMeasuredPosition()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var window = context.Window;
        PrepareActualSubtitle(context);
        var document = window.DocumentSnapshot;
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "Fonts/missing.ttf");
        var line = document.Subtitles[0] with
        {
            Text = "ABC",
            Style = document.Subtitles[0].Style with
            {
                FontFamily = "Missing embedded face", FontAssetId = font.Id, Position = null
            }
        };
        var loaded = ProjectStore.Deserialize(ProjectStore.Serialize(document with
        {
            Assets = document.Assets.Add(font), Subtitles = [line]
        }));
        context.Session.ResetSelection();
        context.Session.Editor.Reset(loaded);
        context.Session.SelectLayer(Assert.Single(loaded.Layers).Id, [Assert.Single(loaded.Layers).Id]);
        await context.Session.RunCommandAsync(() => context.Controller.SeekAsync(MediaTime.Zero));
        Dispatcher.UIThread.RunJobs();

        Assert.Same(loaded, window.DocumentSnapshot);
        Assert.Contains("missing.ttf", Assert.IsType<string>(window.ViewModel.Error), StringComparison.Ordinal);
        Assert.Equal(Assert.Single(loaded.Layers).Id, context.Session.SelectedLayer!.Id);
        Assert.False(window.ViewModel.Styles.Position.CanCustomize);
        Assert.False(window.ViewModel.Effects.CanEditPosition);
        Assert.Null(window.ViewModel.Effects.PositionX);
        Assert.Null(window.ViewModel.Effects.PositionY);
        Assert.Equal(string.Empty, window.ViewModel.Styles.Position.OffsetY.RawText);
        Assert.Equal(font.Id, window.DocumentSnapshot.Subtitles[0].Style.FontAssetId);
        window.ViewModel.Effects.RotationText = "15";
        Assert.True(window.ViewModel.TryCommitDrafts());
        Assert.Equal(15, window.DocumentSnapshot.Layers[0].Transform.Rotation);
        Assert.Null(window.ViewModel.Effects.PositionX);
        window.ViewModel.Styles.CommitFont("sans-serif");
        await context.Session.RunCommandAsync(() => context.Controller.SeekAsync(MediaTime.Zero));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("ABC", window.DocumentSnapshot.Subtitles[0].Text);
        Assert.Null(window.DocumentSnapshot.Subtitles[0].Style.FontAssetId);
        Assert.True(window.ViewModel.Styles.Position.CanCustomize);
        Assert.True(window.ViewModel.Effects.CanEditPosition);
        Assert.NotNull(window.ViewModel.Effects.PositionX);
        Assert.NotNull(window.ViewModel.Effects.PositionY);
        Assert.Null(window.ViewModel.Error);
    }

    [AvaloniaFact]
    public async Task PositionDraftBindsToThePanelAndAbsoluteEffectPositionCommitsAnUndoableDelta()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var window = context.Window;
        PrepareActualSubtitle(context);
        var original = window.DocumentSnapshot;
        var model = window.ViewModel.Styles.Position;
        var originalX = window.ViewModel.Effects.PositionX!.Value;
        var originalY = window.ViewModel.Effects.PositionY!.Value;
        Assert.True(originalY > 0);
        Assert.Equal(0, Assert.Single(original.Layers).Transform.Y);
        window.Layouts.Activate(WorkbenchPanelIds.STYLES);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var explicitCheck = UiTestActions.Find<CheckBox>(window, "ExplicitPositionCheck");
        ClickVisibleControl(window, explicitCheck);
        Dispatcher.UIThread.RunJobs();

        Assert.True(model.IsExplicit);
        Assert.NotNull(Assert.Single(window.DocumentSnapshot.Subtitles).Style.Position);
        Assert.Equal(originalX, window.ViewModel.Effects.PositionX);
        Assert.Equal(originalY, window.ViewModel.Effects.PositionY);
        var offsetBox = FocusVisibleNumericInput(window, WorkbenchPanelIds.STYLES, "OffsetXInput");
        offsetBox.Text = "24";
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.ViewModel.TryCommitDrafts());
        Assert.Equal(24, Assert.Single(window.DocumentSnapshot.Subtitles).Style.Position!.Offset.X);
        var baseX = window.ViewModel.Effects.PositionX!.Value;
        var styleSnapshot = window.DocumentSnapshot;
        var targetX = baseX + 30;
        var x = FocusVisibleNumericInput(window, WorkbenchPanelIds.EFFECTS, "PositionXInput");
        x.Text = targetX.ToString(CultureInfo.CurrentCulture);
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.ViewModel.TryCommitDrafts());
        Assert.Equal(30, Assert.Single(window.DocumentSnapshot.Layers).Transform.X);
        Assert.Equal(targetX, window.ViewModel.Effects.PositionX);
        window.GetCommand(WorkbenchCommand.UNDO).Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Assert.Single(styleSnapshot.Subtitles).Style, Assert.Single(window.DocumentSnapshot.Subtitles).Style);
        Assert.Equal(0, Assert.Single(window.DocumentSnapshot.Layers).Transform.X);
        Assert.Equal(baseX, window.ViewModel.Effects.PositionX);
        Assert.True(window.ViewModel.TryCommitDrafts());
        Assert.Same(styleSnapshot, window.DocumentSnapshot);
    }

    [AvaloniaFact]
    public async Task InvalidPositionRawTextPreventsLayoutSwitchAndKeepsThePositionFieldFocused()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var window = context.Window;
        PrepareActualSubtitle(context);
        window.ViewModel.Styles.Position.IsExplicit = true;
        Assert.True(window.ViewModel.TryCommitDrafts());
        var text = FocusVisibleNumericInput(window, WorkbenchPanelIds.STYLES, "AnchorXInput");
        var input = UiTestActions.Find<NumericDraftInput>(window, "AnchorXInput");
        var validText = input.RawText;
        try
        {
            text.Text = "7e-";
            Dispatcher.UIThread.RunJobs();
            var snapshot = window.DocumentSnapshot;

            Assert.False(await window.Layouts.ApplyPresetAsync(WorkspaceLayoutPresets.TIMING));
            Dispatcher.UIThread.RunJobs();
            Assert.Same(snapshot, window.DocumentSnapshot);
            Assert.Equal("7e-", input.RawText);
            Assert.Equal("styles", window.ViewModel.InvalidPanelId);
            Assert.Equal("AnchorXInput", window.ViewModel.InvalidFieldKey);
            Assert.True(input.IsFocused || text.IsFocused);
        }
        finally
        {
            input.RawText = validText;
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.ViewModel.TryCommitDrafts());
        }
    }

    private static void PrepareActualSubtitle(MainWindowTestContext context)
    {
        var window = context.Window;
        window.GetCommand(WorkbenchCommand.ADD_SUBTITLE).Execute(null);
        context.Session.Editor.Apply("Actual subtitle geometry fixture", document => document with
        {
            Width = 256, Height = 160,
            Subtitles = [Assert.Single(document.Subtitles) with
            {
                Text = "ABC", Style = Assert.Single(document.Subtitles).Style with
                {
                    FontFamily = "sans-serif", FontAssetId = null, Position = null
                }
            }]
        });
        var layer = Assert.Single(window.DocumentSnapshot.Layers);
        context.Session.SelectLayer(layer.Id, [layer.Id]);
        window.Layouts.Activate(WorkbenchPanelIds.STYLES);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Assert.Equal(layer.Id, context.Session.SelectedLayer!.Id);
        Assert.True(window.ViewModel.Styles.HasCue);
    }

    private static TextBox FocusVisibleNumericInput(MainWindow window, string panelId, string name)
    {
        window.Layouts.Activate(panelId);
        var input = UiTestActions.Find<NumericDraftInput>(window, name);
        var text = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        ClickVisibleControl(window, text);
        Assert.True(text.IsFocused);
        return text;
    }

    private static void ClickVisibleControl(MainWindow window, Control control)
    {
        Assert.True(control.IsEffectivelyEnabled);
        control.BringIntoView();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("Position input must be visible in the main window.");
        var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(point));
        Assert.True(ReferenceEquals(hit, control) || hit.GetVisualAncestors().Contains(control));
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
