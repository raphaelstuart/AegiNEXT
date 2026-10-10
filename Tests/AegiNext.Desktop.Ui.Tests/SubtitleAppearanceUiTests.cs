using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Panels.Styles;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleAppearanceUiTests
{
    [AvaloniaTheory]
    [InlineData("LetterSpacingInput", AnimationProperty.LETTER_SPACING, false)]
    [InlineData("FillBlurInput", AnimationProperty.FILL_BLUR, false)]
    [InlineData("StrokeBlurInput", AnimationProperty.STROKE_BLUR, false)]
    [InlineData("LetterSpacingInput", AnimationProperty.LETTER_SPACING, true)]
    [InlineData("FillBlurInput", AnimationProperty.FILL_BLUR, true)]
    [InlineData("StrokeBlurInput", AnimationProperty.STROKE_BLUR, true)]
    public async Task StyleAppearanceInputsPreviewCommitAndRestoreStaticOrAnimatedValues(string field, AnimationProperty property, bool animated)
    {
        await using var context = new MainWindowTestContext();
        var id = Prepare(context);
        if (animated)
        {
            context.Session.Editor.SetKeyframe(context.Session.SelectedLayer!.Id, property, new(new(0), 2));
            context.Session.Editor.SetKeyframe(context.Session.SelectedLayer!.Id, property, new(new(2), 4));
            Assert.True(context.Session.SelectKeyframe(new(context.Session.SelectedLayer!.Id, property, new(2), new(2))));
        }
        var original = context.Session.DocumentSnapshot;
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = 340, Height = 1000, Content = view };
        try
        {
            host.Show();
            host.UpdateLayout();
            var input = UiTestActions.Find<NumericDraftInput>(host, field);
            Assert.Equal(animated ? 4m : 0m, input.Value);
            Type(host, input, "6");
            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.Equal(6, Value(context.Session.PreviewDocument, id, property, animated));
            UiTestActions.Press(host, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(6, Value(context.Session.DocumentSnapshot, id, property, animated));
            if (animated)
            {
                Assert.Equal(original.Subtitles[0].Style, context.Session.SelectedCue!.Style);
                Assert.Contains(property, UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline")
                    .GetAnimationProperties(context.Session.SelectedLayer!.Id));
            }
            UiTestCapture.CaptureExportPanel(host, $"appearance-{property}-{(animated ? "animated" : "static")}");
            Assert.True(context.Session.Editor.Undo());
            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.Equal(animated ? 4m : 0m, input.Value);
            Type(host, input, "7e-");
            Assert.False(context.Session.TryCommitDrafts(false));
            Assert.Equal("7e-", input.RawText);
            UiTestActions.Press(host, Key.Escape);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(animated ? "4" : "0", input.RawText);
            Assert.Same(original, context.Session.DocumentSnapshot);
        }
        finally
        {
            host.Close();
            view.Dispose();
        }
    }

    [AvaloniaTheory]
    [InlineData("LetterSpacingInput", AnimationProperty.LETTER_SPACING)]
    [InlineData("FillBlurInput", AnimationProperty.FILL_BLUR)]
    [InlineData("StrokeBlurInput", AnimationProperty.STROKE_BLUR)]
    public async Task OrderedAppearanceTracksKeepEvaluatedFieldsReadOnlyAndAllowOperationEditing(string field, AnimationProperty property)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        Prepare(context);
        var operation = new AnimationTransformOperation(Guid.NewGuid(), new(0), new(2), 4);
        context.Session.Editor.Apply("Appearance transform fixture", document => document with
        {
            Layers = [document.Layers[0] with
            {
                Tracks = [new(property, []) { InitialValue = 0, Transforms = [operation] }]
            }]
        });
        var layer = context.Session.SelectedLayer!;
        Assert.True(context.Session.SelectKeyframe(new(layer.Id, property, operation.End, operation.End) { OperationId = operation.Id }));
        await context.Session.SeekForEditingAsync(operation.End);
        var original = context.Session.DocumentSnapshot;
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = 340, Height = 1000, Content = view };
        try
        {
            host.Show();
            host.UpdateLayout();
            var input = UiTestActions.Find<NumericDraftInput>(host, field);
            Assert.Equal(4m, input.Value);
            Assert.False(input.IsEffectivelyEnabled);
            context.Session.UpdatePreferences(context.Session.Preferences with { Language = "zh-CN" });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Localization.Get("Workbench.StyleOrderedTransformHint"), ToolTip.GetTip(Assert.IsType<StackPanel>(input.Parent)));
            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.Equal(operation.Id, context.ViewModel.Effects.SelectedOperation?.Id);
            var value = UiTestActions.Find<NumericDraftInput>(context.Window, "OperationValueInput");
            Assert.True(value.IsEffectivelyEnabled);
            Type(context.Window, value, "6");
            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.Equal(6, context.Session.PreviewDocument.Layers[0].Tracks[0].Transforms[0].Value.Scalar);
            UiTestActions.Press(context.Window, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(6, context.Session.SelectedLayer!.Tracks[0].Transforms[0].Value.Scalar);
            Assert.Equal(original.Subtitles[0].Style, context.Session.SelectedCue!.Style);
            UiTestCapture.CaptureExportPanel(context.Window, "appearance-ordered-" + property);
            Assert.True(context.Session.Editor.Undo());
            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.Equal(4m, value.Value);
            Assert.False(input.IsEffectivelyEnabled);
        }
        finally
        {
            host.Close();
            view.Dispose();
        }
    }

    [AvaloniaTheory]
    [InlineData("en-US")]
    [InlineData("zh-CN")]
    public async Task WrapModeChoiceCommitsOnceAndLanguageRefreshPreservesTheSelection(string language)
    {
        await using var context = new MainWindowTestContext();
        Prepare(context);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = 340, Height = 1000, Content = view };
        try
        {
            host.Show();
            var original = context.Session.DocumentSnapshot;
            var input = UiTestActions.Find<ComboBox>(host, "WrapModeInput");
            input.SelectedIndex = (int)SubtitleWrapMode.NATURAL;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(SubtitleWrapMode.NATURAL, context.Session.SelectedCue!.Style.WrapMode);
            var changed = context.Session.DocumentSnapshot;
            context.Session.UpdatePreferences(context.Session.Preferences with { Language = language });
            Dispatcher.UIThread.RunJobs();
            Assert.Same(changed, context.Session.DocumentSnapshot);
            Assert.Equal((int)SubtitleWrapMode.NATURAL, input.SelectedIndex);
            Assert.Equal(Localization.Get("Workbench.WrapNatural"), ((ComboBoxItem)input.Items[1]!).Content);
            UiTestCapture.CaptureExportPanel(host, "appearance-wrap-" + language);
            Assert.True(context.Session.Editor.Undo());
            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.Equal((int)SubtitleWrapMode.GRAPHEME, input.SelectedIndex);
        }
        finally
        {
            host.Close();
            view.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task SelectionAndKaraokeExposeTheirOwnAppearanceFieldsAndCommitOnlyTheSelectedRange()
    {
        await using var context = new MainWindowTestContext();
        var id = Prepare(context);
        Assert.True(context.Session.Details.GenerateAllTiming());
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 1000;
        host.Height = 1100;
        var editor = UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput");
        editor.SetSelection(0, 2);
        Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();
        var spacing = UiTestActions.Find<NumericDraftInput>(host, "SelectionLetterSpacingInput");
        foreach (var expander in spacing.GetVisualAncestors().OfType<Expander>())
        {
            expander.IsExpanded = true;
        }
        var original = context.Session.DocumentSnapshot;
        Type(host, spacing, "-2");
        UiTestActions.Press(host, Key.Enter);
        Dispatcher.UIThread.RunJobs();
        var span = Assert.Single(context.Session.SelectedCue!.InlineSpans);
        Assert.Equal(0, span.Utf16Start);
        Assert.Equal(2, span.Utf16Length);
        Assert.Equal(-2, span.Style.LetterSpacing);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.DocumentSnapshot);
        editor.SetSelection(0, 2);
        UiTestActions.Find<ComboBox>(host, "SubtitleVisualStateInput").SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();
        Assert.False(spacing.IsEffectivelyEnabled);
        var fill = UiTestActions.Find<NumericDraftInput>(host, "SelectionFillBlurInput");
        var stroke = UiTestActions.Find<NumericDraftInput>(host, "SelectionStrokeBlurInput");
        Assert.True(fill.IsEffectivelyVisible);
        Assert.True(stroke.IsEffectivelyVisible);
        Assert.True(fill.IsEffectivelyEnabled);
        Assert.True(stroke.IsEffectivelyEnabled);
        Type(host, fill, "3");
        UiTestActions.Press(host, Key.Enter);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, context.Session.Details.HighlightStyle().FillBlur);
        Assert.Equal(0, context.Session.SelectedCue!.Style.FillBlur);
        Assert.Equal(0, context.Session.SelectedCue.Style.LetterSpacing);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.DocumentSnapshot);
        UiTestCapture.CaptureExportPanel(host, "appearance-karaoke-fields");
        Assert.Equal(id, context.Session.SelectedCueId);
    }

    private static Guid Prepare(MainWindowTestContext context)
    {
        var id = context.Session.Editor.AddSubtitle(new(0), new(4), "字幕 ABC 中文");
        context.Session.Editor.Apply("Subtitle appearance fixture", document => document with
        {
            Width = 640, Height = 360,
            Subtitles = [document.Subtitles[0] with { Style = new() { FontFamily = "sans-serif", FontSize = 28 } }]
        });
        context.Session.SelectCue(id);
        Dispatcher.UIThread.RunJobs();
        return id;
    }

    private static double Value(ProjectDocument document, Guid id, AnimationProperty property, bool animated)
    {
        if (animated)
        {
            return document.Layers.Single(layer => layer.SubtitleId == id).Tracks.Single(track => track.Property == property).Keyframes[^1].Value.Scalar;
        }
        var style = document.Subtitles.Single(line => line.Id == id).Style;
        return property switch
        {
            AnimationProperty.LETTER_SPACING => style.LetterSpacing,
            AnimationProperty.FILL_BLUR => style.FillBlur,
            _ => style.StrokeBlur
        };
    }

    private static void Type(Window window, NumericDraftInput input, string value)
    {
        input.BringIntoView();
        window.UpdateLayout();
        var box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        Assert.True(box.Focus());
        UiTestActions.Press(window, Key.A, RawInputModifiers.Control);
        window.KeyTextInput(value);
        Dispatcher.UIThread.RunJobs();
    }
}
