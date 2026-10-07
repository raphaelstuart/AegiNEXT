using System.Globalization;
using AegiNext.Application.Presets;
using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Workspace;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Styling;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Windowing;
using AegiNext.Rendering.Projects;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace AegiNext.Desktop.Panels.SubtitleDetails;

internal sealed class SubtitleDetailsPanelView : UserControl, IWorkbenchPanelView, IWorkbenchFocusCommandTarget
{
    private readonly WorkbenchSession session;
    private readonly SubtitleDetailsCoordinator coordinator;
    private readonly List<IDisposable> bindings = [];
    private readonly RichSubtitleEditor rich = new() { Name = "RichSubtitleInput", RestoreOnEscape = false };
    private readonly KaraokeClipAxis axis = new() { Name = "KaraokeAxis" };
    private readonly TextBox code = new() { Name = "SubtitleCodeInput", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, IsUndoEnabled = false };
    private readonly TextBlock error = new() { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap };
    private readonly TabControl tabs = new() { Name = "SubtitleDetailsTabs" };
    private readonly NumericDraftInput duration = new() { Name = "KaraokeDurationInput", Width = 200, Minimum = 0, Maximum = 86400, Increment = 0.01m, ShowButtonSpinner = false };
    private readonly ComboBox kind = new() { Name = "KaraokeHighlightKindInput", Width = 200 };
    private readonly ComboBox presets = new() { Name = "SelectionStylePresetCombo", Width = 200 };
    private readonly FontFamilyPicker selectionFont = new() { Name = "SelectionFontInput", Width = 200, RestoreOnEscape = false, CommitOnLostFocus = false };
    private readonly ToolbarToggleButton highlightTarget = new() { Name = "HighlightStyleToggle" };
    private readonly DraftPopup clipPopup = new() { OverlayDismissEventPassThrough = true,
        Placement = PlacementMode.BottomEdgeAlignedLeft, VerticalOffset = 4 };
    private readonly ToolbarToggleButton enableKaraoke = new() { Name = "EnableKaraokeToggle" };
    private readonly ToolbarToggleButton loop = new() { Name = "SubtitleLoopToggle", IsChecked = false };
    private readonly ToolbarToggleButton snap = new() { Name = "KaraokeSnapToggle", IsChecked = true };
    private readonly ToolbarToggleButton keepTimeLabels = new() { Name = "KaraokeTimeLabelsToggle", IsChecked = false };
    private readonly Button play;
    private readonly StackPanel styleToolbar = new() { Name = "SelectionStyleToolbar", Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
    private readonly WrapPanel styleFields = new() { Name = "SelectionStyleFields" };
    private readonly WrapPanel timingFields = new() { Name = "KaraokeTimingFields", Width = 424 };
    private readonly List<Control> bodyOnlyFields = [];
    private readonly List<Button> selectionActions = [];
    private readonly Dictionary<string, ToolbarToggleButton> toggles = [];
    private readonly Dictionary<SubtitleTextAlignment, ToolbarToggleButton> alignmentToggles = [];
    private bool synchronizing;
    private bool disposed;
    private bool completingInput;
    private bool formattingPointerActive;
    private bool formattingFocusPending;
    private Window? hostWindow;
    private int focusRevision;
    private int activeTab;
    private int styleFocusRevision;
    private int selectionStart;
    private int selectionEnd;
    private Guid? previousTarget;
    private long lastPreviewRefresh;
    private Guid? loadedHighlightCueId;
    private KaraokeHighlightStyle? loadedHighlightStyle;
    private bool lastPlaybackState;
    private bool editingHighlight;
    private Guid? bodyPresetId;
    private Guid? highlightPresetId;
    private readonly Guid selectionOverridePresetId = Guid.NewGuid();

    internal SubtitleDetailsPanelView(WorkbenchSession session)
    {
        this.session = session;
        coordinator = session.Details;
        MinHeight = 220;
        Focusable = true;
        code.Classes.Add("multiline-input");
        code.Bind(ThemeProperty, new DynamicResourceExtension("SubtitleCodeTextBoxTheme"));
        Classes.Add("business-surface");
        rich.TextEditRequested += OnTextEdit;
        rich.SelectionChanged += OnSelection;
        rich.RestoreRequested += (_, _) => coordinator.Restore("Text");
        code.PropertyChanged += (_, e) =>
        {
            if (!synchronizing && e.Property == TextBox.TextProperty && (code.Text ?? string.Empty) != coordinator.Source)
            {
                coordinator.EditSource(code.Text ?? string.Empty);
            }
        };
        tabs.SelectionChanged += OnTabChanged;
        axis.ClipSelectionRequested += (_, e) =>
        {
            if (coordinator.SelectClip(e.ClipId) && coordinator.Line?.Karaoke.FirstOrDefault(value => value.Id == e.ClipId) is { } clip)
            {
                SetTextSelection(clip.Utf16Start, clip.Utf16Start + clip.Utf16Length);
                Refresh();
            }
        };
        axis.DurationRequested += (_, e) =>
        {
            if (coordinator.Line?.Id == e.SubtitleId)
            {
                coordinator.SetDuration(e.ClipId, e.Duration);
            }
        };
        axis.LeadingDelayRequested += (_, e) =>
        {
            if (coordinator.Line?.Id == e.SubtitleId)
            {
                coordinator.SetLeadingDelay(e.Delay);
            }
        };
        axis.ClipEditRequested += (_, e) =>
        {
            if (coordinator.Line?.Id == e.SubtitleId && coordinator.SelectedClipId == e.ClipId)
            {
                clipPopup.PlacementRect = e.Anchor;
                clipPopup.ShowAt(axis);
            }
        };
        clipPopup.Closing += (sender, e) =>
        {
            e.Cancel = !coordinator.TryPrepare(session.Editor.Snapshot, out _);
            if (!e.Cancel)
            {
                QueueStyleFocusCommit();
            }
        };
        code.PropertyChanged += (_, e) =>
        {
            if (!synchronizing && activeTab == 1 && e.Property is { } property &&
                (property == TextBox.SelectionStartProperty || property == TextBox.SelectionEndProperty))
            {
                var entries = coordinator.SourceMap.Where(entry => entry.Utf16Length > 0).ToArray();
                if (entries.Length > 0)
                {
                    int Offset(int sourceOffset)
                    {
                        var entry = entries.FirstOrDefault(entry => entry.SourceStart + entry.SourceLength >= sourceOffset) ?? entries[^1];
                        return entry.Utf16Start + (sourceOffset >= entry.SourceStart + entry.SourceLength ? entry.Utf16Length : 0);
                    }
                    SetTextSelection(Offset(code.SelectionStart), Offset(code.SelectionEnd));
                }
            }
        };
        duration.PropertyChanged += (_, e) =>
        {
            if (!synchronizing && e.Property == NumericDraftInput.RawTextProperty)
            {
                coordinator.EditDuration(duration.RawText);
            }
        };
        duration.LostFocus += (_, _) =>
        {
            if (!completingInput)
            {
                if (formattingPointerActive)
                {
                    formattingFocusPending = true;
                }
                else
                {
                    QueueStyleFocusCommit();
                }
            }
        };
        kind.SelectionChanged += (_, _) =>
        {
            if (!synchronizing && kind.SelectedIndex >= 0)
            {
                coordinator.SetHighlightKind((KaraokeHighlightKind)kind.SelectedIndex);
            }
        };
        play = IconButton("Workbench.PlaySegment", "Play", async () =>
        {
            if (coordinator.IsPlaying)
            {
                await coordinator.StopPlaybackAsync();
            }
            else if (HasFocusWithin())
            {
                await coordinator.PlayAsync(coordinator.SelectedClipId is not null, loop.IsChecked == true);
            }
        });
        play.Name = "SubtitlePlayPauseButton";
        ConfigureToggle(loop, "Workbench.LoopPlayback", WorkbenchIcon.Create("Loop"));
        loop.PropertyChanged += async (_, e) =>
        {
            if (!synchronizing && e.Property == ToggleButton.IsCheckedProperty)
            {
                await session.RunCommandAsync(() => coordinator.SetLoopEnabledAsync(loop.IsChecked == true));
            }
        };
        ConfigureToggle(snap, "Workbench.KaraokeSnap", WorkbenchIcon.Create("Magnet"), "Workbench.KaraokeSnapHint");
        ConfigureToggle(keepTimeLabels, "Workbench.KaraokeKeepTimeLabels", WorkbenchIcon.Create("Clock"), "Workbench.KaraokeKeepTimeLabelsHint");
        ConfigureToggle(enableKaraoke, "Workbench.EnableKaraoke", IconLabel("Workbench.EnableKaraoke", "EnableHighlight"));
        enableKaraoke.Width = double.NaN;
        enableKaraoke.Padding = new(8, 0);
        ConfigureToggle(highlightTarget, "Workbench.HighlightStyleTarget", WorkbenchIcon.Create("HighlightStyle"));
        highlightTarget.PropertyChanged += (_, e) =>
        {
            if (!synchronizing && e.Property == ToggleButton.IsCheckedProperty)
            {
                var requested = highlightTarget.IsChecked == true;
                if (coordinator.TryCommit())
                {
                    editingHighlight = requested && coordinator.IsKaraokeEnabled;
                }
                Refresh();
            }
        };
        BuildStyleFields();
        styleToolbar.Children.Add(ToolbarSeparator("PlaybackActionSeparator"));
        styleToolbar.Children.Add(play);
        styleToolbar.Children.Add(loop);
        styleToolbar.Children.Add(snap);
        styleToolbar.Children.Add(keepTimeLabels);
        styleToolbar.Children.Add(highlightTarget);
        var restore = Button("Workbench.RestoreDraft", "Reset", () =>
        {
            coordinator.Restore("All");
            return Task.CompletedTask;
        });
        restore.Margin = new(0);
        restore.Height = restore.MinHeight = 32;
        restore.VerticalAlignment = VerticalAlignment.Center;
        var toolbar = new Grid { Name = "SubtitleDetailsToolbar", ColumnDefinitions = new("*,Auto") };
        toolbar.Children.Add(new ScrollViewer
        {
            Name = "SelectionToolbarScroll", Content = styleToolbar, Height = 32,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        var actions = new StackPanel
        {
            Name = "SubtitleDetailsActions",
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { enableKaraoke, restore }
        };
        Grid.SetColumn(actions, 1);
        toolbar.Children.Add(actions);
        enableKaraoke.PropertyChanged += (_, e) =>
        {
            if (!synchronizing && e.Property == ToggleButton.IsCheckedProperty)
            {
                coordinator.SetKaraokeEnabled(enableKaraoke.IsChecked == true);
                Refresh();
            }
        };
        timingFields.Children.Add(Field("Workbench.ClipDuration", duration));
        timingFields.Children.Add(Field("Workbench.HighlightBehavior", kind));
        snap.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty)
            {
                axis.IsSnapEnabled = snap.IsChecked == true;
            }
        };
        keepTimeLabels.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty)
            {
                axis.KeepDurationLabelsVisible = keepTimeLabels.IsChecked == true;
            }
        };
        var popupFrame = new Border { Padding = new(12), BorderThickness = new(1), CornerRadius = new(6), Child = timingFields };
        popupFrame.Bind(Border.BackgroundProperty, new DynamicResourceExtension("PreviewSurface"));
        popupFrame.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("PreviewBorder"));
        popupFrame.Classes.Add("business-surface");
        popupFrame.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape || !clipPopup.IsOpen)
            {
                return;
            }
            completingInput = true;
            try
            {
                coordinator.Restore("Duration");
                clipPopup.ForceClose();
                axis.Focus();
                e.Handled = true;
            }
            finally
            {
                completingInput = false;
            }
        }, RoutingStrategies.Tunnel, true);
        clipPopup.Child = popupFrame;
        FlyoutBase.SetAttachedFlyout(axis, clipPopup);
        var richContent = new Grid { RowDefinitions = new("Auto,Auto"), RowSpacing = 4 };
        rich.MinHeight = 96;
        richContent.Children.Add(rich);
        Grid.SetRow(axis, 1);
        richContent.Children.Add(axis);
        tabs.Items.Add(Tab("Workbench.RichText", richContent));
        code.MinHeight = 96;
        ScrollViewer.SetHorizontalScrollBarVisibility(code, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(code, ScrollBarVisibility.Disabled);
        tabs.Items.Add(Tab("Workbench.AdvancedCode", code));
        var root = new SubtitleDetailsLayout(toolbar, styleFields, tabs, error);
        Content = new ScrollViewer
        {
            Name = "SubtitleDetailsScroll", Content = root,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        presets.SelectionChanged += async (_, _) =>
        {
            if (!synchronizing && presets.SelectedItem is StylePresetListItem item)
            {
                if (editingHighlight)
                {
                    await session.RunCommandAsync(ApplyHighlightPresetAsync);
                }
                else
                {
                    bodyPresetId = item.Id;
                }
            }
        };
        styleFields.AddHandler(KeyDownEvent, OnStyleFieldKeyDown, RoutingStrategies.Bubble, true);
        styleFields.AddHandler(LostFocusEvent, OnStyleFieldLostFocus, RoutingStrategies.Bubble);
        foreach (var input in new[] { duration })
        {
            input.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    coordinator.TryCommit();
                    e.Handled = true;
                }
            };
        }
        coordinator.Changed += OnCoordinatorChanged;
        session.StyleLibraryChanged += OnStyleLibraryChanged;
        session.ViewModel.GesturesCancelled += OnGesturesCancelled;
        session.PreviewUpdated += OnPreviewUpdated;
        Localization.LanguageChanged += OnLanguageChanged;
        AddHandler(GotFocusEvent, OnPanelGotFocus, RoutingStrategies.Bubble);
        AddHandler(LostFocusEvent, OnPanelLostFocus, RoutingStrategies.Bubble);
        AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel, true);
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.Source is Control source && source.GetSelfAndVisualAncestors().Any(value => value is ToolbarToggleButton button &&
                (toggles.ContainsValue(button) || alignmentToggles.ContainsValue(button))))
            {
                formattingPointerActive = true;
                formattingFocusPending = false;
                ++styleFocusRevision;
            }
        }, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, (_, _) => EndFormattingPointer(), RoutingStrategies.Tunnel, true);
        coordinator.Synchronize();
        Refresh();
    }

    private void BuildStyleFields()
    {
        foreach (var (name, text) in new[] { ("Bold", "B"), ("Italic", "I"), ("Underline", "U"), ("Strikethrough", "S") })
        {
            var button = new ToolbarToggleButton { Name = name + "SelectionButton" };
            ConfigureToggle(button, "Workbench." + name, text);
            button.Click += (_, _) => Toggle(name);
            button.PointerCaptureLost += (_, _) => EndFormattingPointer();
            toggles.Add(name, button);
            styleToolbar.Children.Add(button);
            bodyOnlyFields.Add(button);
        }
        styleToolbar.Children.Add(ToolbarSeparator("SelectionActionSeparator"));
        styleToolbar.Children.Add(SelectionAction("Workbench.ApplyToSelection", "ApplySelectionPreset", ApplyPresetAsync));
        styleToolbar.Children.Add(SelectionAction("Workbench.ApplySelectionStyle", "ApplySelectionStyle", () =>
        {
            coordinator.TryCommit();
            return Task.CompletedTask;
        }));
        styleToolbar.Children.Add(SelectionAction("Workbench.ClearSelectionStyle", "ClearSelectionStyle", () =>
        {
            if (editingHighlight)
            {
                coordinator.ApplyHighlightStyle(null);
            }
            else
            {
                coordinator.ClearSelectionStyle(Math.Min(selectionStart, selectionEnd), Math.Abs(selectionEnd - selectionStart));
            }
            return Task.CompletedTask;
        }));
        styleToolbar.Children.Add(ToolbarSeparator("TextAlignmentSeparator"));
        foreach (var (alignment, key) in new[]
        {
            (SubtitleTextAlignment.LEFT, "AlignLeft"),
            (SubtitleTextAlignment.CENTER, "AlignCenter"),
            (SubtitleTextAlignment.RIGHT, "AlignRight")
        })
        {
            var button = new ToolbarToggleButton { Name = key + "Button" };
            ConfigureToggle(button, "Workbench." + key, WorkbenchIcon.Create(key));
            button.Click += (_, _) =>
            {
                coordinator.ApplyTextAlignment(alignment);
                Refresh();
            };
            button.PointerCaptureLost += (_, _) => EndFormattingPointer();
            alignmentToggles.Add(alignment, button);
            styleToolbar.Children.Add(button);
        }
        var font = selectionFont;
        font.RefreshFontCandidates(session.Fonts.Candidates);
        font.SetCurrentFont(SubtitleFontSelectionService.FromStyle(coordinator.SelectionStyle()));
        bindings.Add(font.Bind(AutoCompleteBox.TextProperty, new Binding(nameof(SubtitleDetailsStyleDraft.FontFamily))
            { Source = coordinator.StyleDraft, Mode = BindingMode.TwoWay }));
        font.FamilyCommitted += (_, value) =>
        {
            coordinator.StyleDraft.SelectFont(value.Selection);
            coordinator.CompleteInput(nameof(SubtitleDetailsStyleDraft.FontFamily), false);
        };
        var fontField = Field("Workbench.Font", font);
        var fontSizeField = Field("Workbench.FontSize", Number(coordinator.StyleDraft, nameof(SubtitleDetailsStyleDraft.FontSizeText), "SelectionFontSizeInput", 0.01m, 4096));
        bodyOnlyFields.Add(fontField);
        bodyOnlyFields.Add(fontSizeField);
        styleFields.Children.Add(fontField);
        styleFields.Children.Add(fontSizeField);
        styleFields.Children.Add(Field("Workbench.Fill", StyleColor(nameof(SubtitleDetailsStyleDraft.Fill), "SelectionFillInput")));
        styleFields.Children.Add(Field("Workbench.Stroke", StyleColor(nameof(SubtitleDetailsStyleDraft.Stroke), "SelectionStrokeInput")));
        styleFields.Children.Add(Field("Workbench.StrokeWidth", Number(null, nameof(SubtitleDetailsStyleDraft.StrokeWidthText), "SelectionStrokeWidthInput", 0, 4096)));
        styleFields.Children.Add(Field("Workbench.Shadow", StyleColor(nameof(SubtitleDetailsStyleDraft.Shadow), "SelectionShadowInput")));
        styleFields.Children.Add(ShadowOffset(null, "Selection"));
        styleFields.Children.Add(Field("Workbench.ShadowBlur", Number(null, nameof(SubtitleDetailsStyleDraft.ShadowBlurText), "SelectionShadowBlurInput", 0, 512)));
        presets.DisplayMemberBinding = new Binding("Name");
        styleFields.Children.Add(Field("Workbench.StylePreset", presets));
    }

    private ColorDraftInput StyleColor(string property, string name)
    {
        var input = new ColorDraftInput { Name = name, Width = 200, RestoreOnEscape = false, CommitOnLostFocus = false };
        bindings.Add(input.Bind(ColorDraftInput.DraftProperty, new Binding(property)));
        return input;
    }

    private StackPanel ShadowOffset(object? draft, string prefix)
    {
        var vector = new Grid { Name = prefix + "ShadowOffsetInput", ColumnDefinitions = new("Auto,*,Auto,*"), ColumnSpacing = 6 };
        var column = 0;
        foreach (var (axisName, property) in new[] { ("X", nameof(SubtitleDetailsStyleDraft.ShadowXText)), ("Y", nameof(SubtitleDetailsStyleDraft.ShadowYText)) })
        {
            var label = new TextBlock { Text = axisName, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, column++);
            vector.Children.Add(label);
            var input = Number(draft, property, prefix + "Shadow" + axisName + "Input", -4096, 4096);
            input.Width = double.NaN;
            input.MinWidth = 0;
            Grid.SetColumn(input, column++);
            vector.Children.Add(input);
        }
        return Field("Workbench.ShadowOffset", vector);
    }

    private NumericDraftInput Number(object? draft, string property, string name, decimal minimum, decimal maximum)
    {
        var input = new NumericDraftInput { Name = name, Width = 200, Minimum = minimum, Maximum = maximum, Increment = 1,
            ShowButtonSpinner = false, NumberFormat = CultureInfo.InvariantCulture.NumberFormat };
        var valueBinding = DraftBinding(draft, property, BindingMode.OneWay);
        valueBinding.Converter = new FuncValueConverter<string, object>(text =>
            decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : BindingOperations.DoNothing);
        bindings.Add(input.Bind(NumericUpDown.ValueProperty, valueBinding));
        bindings.Add(input.Bind(NumericDraftInput.RawTextProperty, DraftBinding(draft, property, BindingMode.TwoWay)));
        return input;
    }

    private static Binding DraftBinding(object? draft, string property, BindingMode mode)
    {
        var binding = new Binding(property) { Mode = mode };
        if (draft is not null)
        {
            binding.Source = draft;
        }
        return binding;
    }

    private StackPanel Field(string key, Control input)
    {
        var field = new StackPanel { Name = input.Name is null ? null : input.Name + "Field", Spacing = 4, Width = 200, Margin = new Thickness(0, 0, 12, 8) };
        field.Children.Add(Label(key));
        field.Children.Add(input);
        return field;
    }

    private TextBlock Label(string key)
    {
        var label = new TextBlock();
        label.Classes.Add("field");
        bindings.Add(label.Bind(TextBlock.TextProperty, Localization.Observe(key).ToBinding()));
        return label;
    }

    private Button Button(string key, string icon, Func<Task> action)
    {
        var button = new Button
        {
            Name = key[(key.LastIndexOf('.') + 1)..] + "Button",
            Content = IconLabel(key, icon),
            Margin = new Thickness(0, 0, 6, 6)
        };
        bindings.Add(button.Bind(ToolTip.TipProperty, Localization.Observe(key).ToBinding()));
        bindings.Add(button.Bind(AutomationProperties.NameProperty, Localization.Observe(key).ToBinding()));
        button.Click += async (_, _) => await session.RunCommandAsync(action);
        return button;
    }

    private IconText IconLabel(string key, string icon)
    {
        var content = new IconText { IconKey = icon };
        bindings.Add(content.Bind(IconText.TextProperty, Localization.Observe(key).ToBinding()));
        return content;
    }

    private Button IconButton(string key, string icon, Func<Task> action)
    {
        var button = new Button { Name = key[(key.LastIndexOf('.') + 1)..] + "Button", Content = WorkbenchIcon.Create(icon) };
        button.Classes.Add("icon-button");
        button.Click += async (_, _) => await session.RunCommandAsync(action);
        return button;
    }

    private Button SelectionAction(string key, string icon, Func<Task> action)
    {
        var button = IconButton(key, icon, action);
        bindings.Add(button.Bind(ToolTip.TipProperty, Localization.Observe(key + "Hint").ToBinding()));
        bindings.Add(button.Bind(AutomationProperties.NameProperty, Localization.Observe(key).ToBinding()));
        selectionActions.Add(button);
        return button;
    }

    private void ConfigureToggle(ToolbarToggleButton toggle, string key, object content, string? hintKey = null)
    {
        toggle.Content = content;
        bindings.Add(toggle.Bind(ToolTip.TipProperty, Localization.Observe(hintKey ?? key).ToBinding()));
        bindings.Add(toggle.Bind(AutomationProperties.NameProperty, Localization.Observe(key).ToBinding()));
    }

    private static Separator ToolbarSeparator(string name) => new()
    {
        Name = name, Width = 1, Height = 24, Margin = new(4, 0), VerticalAlignment = VerticalAlignment.Center,
        Template = new FuncControlTemplate<Separator>((_, _) =>
        {
            var line = new Border();
            line.Bind(Border.BackgroundProperty, new DynamicResourceExtension("PreviewBorder"));
            return line;
        })
    };

    private TabItem Tab(string key, Control content)
    {
        var tab = new TabItem { Content = content };
        bindings.Add(tab.Bind(HeaderedContentControl.HeaderProperty, Localization.Observe(key).ToBinding()));
        return tab;
    }

    private void Toggle(string name)
    {
        var requested = toggles[name].IsChecked == true;
        coordinator.ApplySelectionFormatting(Math.Min(selectionStart, selectionEnd), Math.Abs(selectionEnd - selectionStart), style => name switch
        {
            "Bold" => session.Fonts.ToggleBold(style, requested),
            "Italic" => session.Fonts.ToggleItalic(style, requested),
            "Underline" => new SubtitleInlineStyleOverride { Underline = requested },
            _ => new SubtitleInlineStyleOverride { Strikethrough = requested }
        });
    }

    private async Task ApplyPresetAsync()
    {
        if (editingHighlight)
        {
            await ApplyHighlightPresetAsync();
            return;
        }
        if (presets.SelectedItem is not StylePresetListItem selected || coordinator.Line is not { } line || !coordinator.TryCommit())
        {
            return;
        }
        var start = Math.Min(selectionStart, selectionEnd);
        var length = Math.Abs(selectionEnd - selectionStart);
        var snapshot = session.Editor.Snapshot;
        var prepared = await SubtitleStylePresetService.PrepareAsync(session.StyleLibrary.Snapshot.Presets.Single(value => value.Id == selected.Id),
            snapshot, session.ProjectDirectory);
        if (coordinator.Line?.Id != line.Id || !ReferenceEquals(snapshot, session.Editor.Snapshot))
        {
            return;
        }
        session.Editor.Apply("Apply subtitle selection preset", _ => ProjectEditingOperations.ApplySubtitleInlineStyle(
            prepared.Project, line.Id, start, length,
            SubtitleInlineStyleOverride.FromStyle(prepared.Style)));
    }

    private Task ApplyHighlightPresetAsync()
    {
        if ((presets.SelectedItem as StylePresetListItem)?.Id == selectionOverridePresetId)
        {
            return Task.CompletedTask;
        }
        if (presets.SelectedItem is not StylePresetListItem selected || coordinator.Line is not { } line || !coordinator.TryCommit())
        {
            return Task.CompletedTask;
        }
        var snapshot = session.Editor.Snapshot;
        var preset = session.StyleLibrary.Snapshot.Presets.FirstOrDefault(value => value.Id == selected.Id);
        var value = selected.Id == Guid.Empty ? null : preset is not null
            ? KaraokeHighlightStyle.FromStyle(preset.Id, preset.Name, preset.Style)
            : line.KaraokeStyle?.PresetId == selected.Id ? line.KaraokeStyle : null;
        if (coordinator.Line?.Id == line.Id && ReferenceEquals(snapshot, session.Editor.Snapshot))
        {
            coordinator.ApplyHighlightStyle(value);
        }
        return Task.CompletedTask;
    }

    private void RefreshHighlightPresets(SubtitleLine? line)
    {
        var style = line?.KaraokeStyle;
        var targetChanged = line?.Id != loadedHighlightCueId || style != loadedHighlightStyle;
        var id = targetChanged ? style?.PresetId ?? Guid.Empty : highlightPresetId ?? style?.PresetId ?? Guid.Empty;
        loadedHighlightCueId = line?.Id;
        loadedHighlightStyle = style;
        var options = new List<StylePresetListItem> { new(Guid.Empty, Localization.Get("Workbench.DefaultKaraokeStyle")) };
        options.AddRange(session.ViewModel.Styles.Presets);
        var start = Math.Min(selectionStart, selectionEnd);
        var end = Math.Max(selectionStart, selectionEnd);
        if (start != end && line?.Karaoke.Any(clip => clip.Utf16Start < end &&
            clip.Utf16Start + clip.Utf16Length > start && clip.ActiveStyle is { HasOverrides: true }) == true)
        {
            options.Add(new(selectionOverridePresetId, Localization.Get("Workbench.CustomKaraokeStyle")));
            id = selectionOverridePresetId;
        }
        if (style is not null && options.All(value => value.Id != style.PresetId))
        {
            options.Add(new(style.PresetId, style.PresetName));
        }
        presets.ItemsSource = options;
        presets.SelectedItem = options.FirstOrDefault(value => value.Id == id) ?? options[0];
        highlightPresetId = (presets.SelectedItem as StylePresetListItem)?.Id;
    }

    private void OnTextEdit(object? sender, SubtitleTextEditEventArgs e)
    {
        coordinator.EditText(e.Start, e.Length, e.Replacement);
    }

    private void OnStyleFieldKeyDown(object? sender, KeyEventArgs e)
    {
        var field = StyleField(e.Source as Control);
        if (field is null)
        {
            return;
        }
        if (e.Key == Key.Enter)
        {
            ++styleFocusRevision;
            coordinator.TryCommit();
            e.Handled = true;
        }
    }

    private void OnStyleFieldLostFocus(object? sender, RoutedEventArgs e)
    {
        if (synchronizing || completingInput || StyleField(e.Source as Control) is null)
        {
            return;
        }
        if (formattingPointerActive)
        {
            formattingFocusPending = true;
            return;
        }
        QueueStyleFocusCommit();
    }

    private void EndFormattingPointer()
    {
        if (!formattingPointerActive)
        {
            return;
        }
        formattingPointerActive = false;
        if (formattingFocusPending)
        {
            formattingFocusPending = false;
            QueueStyleFocusCommit();
        }
    }

    private void QueueStyleFocusCommit()
    {
        var revision = ++styleFocusRevision;
        var draft = styleFields.DataContext;
        var document = session.DocumentSnapshot;
        var root = hostWindow;
        Dispatcher.UIThread.Post(() =>
        {
            if (!disposed && revision == styleFocusRevision && ReferenceEquals(draft, styleFields.DataContext) &&
                root is not null && ReferenceEquals(root, hostWindow) &&
                ReferenceEquals(document, session.DocumentSnapshot) &&
                !styleFields.GetVisualDescendants().OfType<Button>().Any(button => button.Flyout?.IsOpen == true))
            {
                if (formattingPointerActive)
                {
                    formattingFocusPending = true;
                    return;
                }
                coordinator.TryCommit();
            }
        }, DispatcherPriority.Background);
    }

    private static string? StyleField(Control? source)
    {
        var ancestors = source?.GetSelfAndVisualAncestors().OfType<Control>().ToArray() ?? [];
        var numeric = ancestors.OfType<NumericDraftInput>().FirstOrDefault();
        if (numeric?.Name is { } name)
        {
            return name switch
            {
                "SelectionFontSizeInput" => nameof(SubtitleDetailsStyleDraft.FontSizeText),
                "SelectionStrokeWidthInput" => nameof(SubtitleDetailsStyleDraft.StrokeWidthText),
                "SelectionShadowXInput" => nameof(SubtitleDetailsStyleDraft.ShadowXText),
                "SelectionShadowYInput" => nameof(SubtitleDetailsStyleDraft.ShadowYText),
                "SelectionShadowBlurInput" => nameof(SubtitleDetailsStyleDraft.ShadowBlurText),
                _ => null
            };
        }
        if (ancestors.OfType<ColorDraftInput>().FirstOrDefault() is { } color)
        {
            return color.Name switch
            {
                "SelectionFillInput" => nameof(SubtitleDetailsStyleDraft.Fill),
                "SelectionStrokeInput" => nameof(SubtitleDetailsStyleDraft.Stroke),
                _ => nameof(SubtitleDetailsStyleDraft.Shadow)
            };
        }
        return ancestors.OfType<FontFamilyPicker>().Any() ? nameof(SubtitleDetailsStyleDraft.FontFamily) : null;
    }

    private void OnSelection(object? sender, EventArgs e)
    {
        if (synchronizing || sender is not RichSubtitleEditor editor)
        {
            return;
        }
        var start = editor.SelectionStart;
        var end = editor.SelectionEnd;
        if (!coordinator.SetStyleSelection(Math.Min(start, end), Math.Abs(end - start)) ||
            !coordinator.FollowTextSelection(Math.Min(start, end), Math.Abs(end - start)))
        {
            SetTextSelection(selectionStart, selectionEnd);
            return;
        }
        selectionStart = start;
        selectionEnd = end;
        session.SetTextCaret(coordinator.Line?.Id ?? Guid.Empty, end);
        Refresh();
    }

    private void SetTextSelection(int start, int end)
    {
        synchronizing = true;
        try
        {
            selectionStart = start;
            selectionEnd = end;
            rich.SetSelection(start, end);
        }
        finally
        {
            synchronizing = false;
        }
        coordinator.SetStyleSelection(Math.Min(start, end), Math.Abs(end - start));
    }

    private void OnTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (synchronizing || tabs.SelectedIndex == activeTab)
        {
            return;
        }
        if (!coordinator.TryCommit())
        {
            synchronizing = true;
            tabs.SelectedIndex = activeTab;
            synchronizing = false;
            return;
        }
        activeTab = tabs.SelectedIndex;
        if (activeTab != 0)
        {
            clipPopup.ForceClose();
        }
        SetTextSelection(selectionStart, selectionEnd);
        Refresh();
        if (activeTab == 1)
        {
            synchronizing = true;
            var entries = coordinator.SourceMap.Where(entry => entry.Utf16Length > 0).ToArray();
            var first = entries.FirstOrDefault(entry => entry.Utf16Start + entry.Utf16Length > Math.Min(selectionStart, selectionEnd));
            var last = entries.LastOrDefault(entry => entry.Utf16Start < Math.Max(selectionStart, selectionEnd));
            code.SelectionStart = first?.SourceStart ?? coordinator.Source.Length;
            code.SelectionEnd = last is null ? code.SelectionStart : last.SourceStart + last.SourceLength;
            synchronizing = false;
        }
    }

    private void Refresh()
    {
        synchronizing = true;
        try
        {
            var line = coordinator.Line;
            if (line?.Id != previousTarget)
            {
                clipPopup.ForceClose();
                previousTarget = line?.Id;
                selectionStart = selectionEnd = 0;
            }
            if (code.Text != coordinator.Source)
            {
                var caret = code.CaretIndex;
                code.Text = coordinator.Source;
                code.CaretIndex = Math.Min(caret, coordinator.Source.Length);
            }
            if (decimal.TryParse(coordinator.DurationText, NumberStyles.Float, CultureInfo.InvariantCulture, out var clipDuration))
            {
                duration.Value = clipDuration;
            }
            duration.RawText = coordinator.DurationText;
            var clip = line?.Karaoke.FirstOrDefault(item => item.Id == coordinator.SelectedClipId);
            kind.ItemsSource = Enum.GetValues<KaraokeHighlightKind>().Select(value => Localization.Get("Workbench.KaraokeKind." + value)).ToArray();
            kind.SelectedIndex = clip is null ? -1 : (int)clip.HighlightKind;
            kind.IsEnabled = duration.IsEnabled = clip is not null;
            axis.SetContent(line, line is null ? MediaTime.Zero : line.Start - coordinator.ContentOrigin, coordinator.SelectedClipId);
            var localTime = session.ProjectPosition - coordinator.ContentOrigin;
            if (!coordinator.IsKaraokeEnabled)
            {
                editingHighlight = false;
            }
            rich.SetContent(coordinator.PreviewDocument, line, session.ProjectDirectory, localTime >= MediaTime.Zero ? localTime : MediaTime.Zero,
                editingHighlight ? SubtitlePreviewMode.HIGHLIGHTED : SubtitlePreviewMode.NORMAL);
            rich.SetSelection(selectionStart, selectionEnd);
            error.Text = coordinator.Error ?? (tabs.SelectedIndex == 1 ? coordinator.SourceDiagnostic : rich.RenderDiagnostic);
            error.IsVisible = !string.IsNullOrEmpty(error.Text);
            play.IsEnabled = line is not null && session.Controller.MediaInfo is not null;
            if (coordinator.IsPlaying != lastPlaybackState)
            {
                play.Content = WorkbenchIcon.Create(coordinator.IsPlaying ? "Pause" : "Play");
            }
            var playbackHint = Localization.Get(coordinator.IsPlaying ? "Workbench.Pause" : "Workbench.PlaySegment");
            ToolTip.SetTip(play, playbackHint);
            AutomationProperties.SetName(play, playbackHint);
            lastPlaybackState = coordinator.IsPlaying;
            enableKaraoke.IsEnabled = line is not null;
            enableKaraoke.IsChecked = coordinator.IsKaraokeEnabled;
            axis.IsVisible = coordinator.IsKaraokeEnabled && activeTab == 0;
            highlightTarget.IsEnabled = coordinator.IsKaraokeEnabled;
            if (!coordinator.IsKaraokeEnabled)
            {
                editingHighlight = false;
                clipPopup.ForceClose();
            }
            highlightTarget.IsChecked = editingHighlight;
            styleFields.DataContext = editingHighlight ? coordinator.HighlightDraft : coordinator.StyleDraft;
            rich.IsEnabled = line is not null;
            code.IsEnabled = line is not null && coordinator.CanEditSource;
            var start = Math.Min(selectionStart, selectionEnd);
            var end = Math.Max(selectionStart, selectionEnd);
            var hasHighlightTarget = start == end || coordinator.SelectionHasTimedKaraoke;
            styleFields.IsEnabled = line is not null && (editingHighlight ? hasHighlightTarget : start != end);
            foreach (var field in bodyOnlyFields)
            {
                field.IsEnabled = line is not null && !editingHighlight && selectionStart != selectionEnd;
            }
            foreach (var action in selectionActions)
            {
                action.IsEnabled = styleFields.IsEnabled;
            }
            var textAlignment = line is null ? (SubtitleTextAlignment?)null :
                line.Style.TextAlign ?? (SubtitleTextAlignment)((int)line.Style.Alignment % 3);
            foreach (var pair in alignmentToggles)
            {
                pair.Value.IsEnabled = line is not null && !editingHighlight;
                pair.Value.IsChecked = pair.Key == textAlignment;
            }
            if (editingHighlight)
            {
                RefreshHighlightPresets(line);
            }
            else
            {
                presets.ItemsSource = session.ViewModel.Styles.Presets;
                presets.SelectedItem = session.ViewModel.Styles.Presets.FirstOrDefault(value => value.Id == bodyPresetId) ?? session.ViewModel.Styles.SelectedPreset;
            }
            var style = coordinator.SelectionStyle();
            if (!coordinator.StyleDraft.IsFieldDirty(nameof(SubtitleDetailsStyleDraft.FontFamily)))
            {
                selectionFont.SetCurrentFont(SubtitleFontSelectionService.FromStyle(style));
            }
            foreach (var pair in toggles)
            {
                pair.Value.IsChecked = pair.Key switch
                {
                    "Bold" => style.Bold,
                    "Italic" => style.Italic,
                    "Underline" => style.Underline,
                    _ => style.Strikethrough
                };
            }
        }
        finally
        {
            synchronizing = false;
        }
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0 || e.Key is not (Key.Z or Key.Y))
        {
            return;
        }
        e.Handled = true;
        if (coordinator.TryCommit())
        {
            if (e.Key == Key.Y || (e.KeyModifiers & KeyModifiers.Shift) != 0)
            {
                session.Editor.Redo();
            }
            else
            {
                session.Editor.Undo();
            }
        }
    }

    private void OnCoordinatorChanged(object? sender, EventArgs e) => Refresh();
    private void OnStyleLibraryChanged(object? sender, EventArgs e) => Refresh();
    private void OnLanguageChanged(object? sender, EventArgs e) => Refresh();
    private void OnGesturesCancelled(object? sender, EventArgs e) => CancelGestures();
    private void OnPreviewUpdated(object? sender, VideoPreviewUpdate e)
    {
        if (coordinator.IsPlaying != lastPlaybackState && HasFocusWithin())
        {
            Refresh();
            return;
        }
        if (coordinator.IsPlaying && HasFocusWithin() && Environment.TickCount64 - lastPreviewRefresh >= 100)
        {
            lastPreviewRefresh = Environment.TickCount64;
            Refresh();
        }
    }
    public string PanelId => WorkbenchPanelIds.SUBTITLE_DETAILS;

    /// <summary>判断当前焦点是否属于可结束输入的详情字段。</summary>
    public bool CanExecuteFocusCommand(WorkbenchCommand command, IInputElement focusedElement)
    {
        return command == WorkbenchCommand.END_TEXT_INPUT && !rich.IsSelectionGestureActive &&
            InputField(focusedElement as Control) is not null;
    }

    /// <summary>完成当前字段并将焦点保留在详情面板，阻止旧失焦回调重复提交。</summary>
    public bool TryExecuteFocusCommand(WorkbenchCommand command, IInputElement focusedElement)
    {
        if (command != WorkbenchCommand.END_TEXT_INPUT || InputField(focusedElement as Control) is not { } field)
        {
            return false;
        }
        ++styleFocusRevision;
        ++focusRevision;
        completingInput = true;
        try
        {
            if (!coordinator.CompleteInput(field, editingHighlight))
            {
                return false;
            }
            return Focus();
        }
        finally
        {
            completingInput = false;
        }
    }

    private string? InputField(Control? source)
    {
        var ancestors = source?.GetSelfAndVisualAncestors().OfType<Control>().ToArray() ?? [];
        if (ancestors.Contains(rich))
        {
            return "Text";
        }
        if (ancestors.Contains(code))
        {
            return "Code";
        }
        if (ancestors.Contains(duration))
        {
            return "Duration";
        }
        return StyleField(source);
    }

    /// <summary>取消面板内未提交的指针手势。</summary>
    public void CancelGestures()
    {
        formattingPointerActive = formattingFocusPending = false;
        ++styleFocusRevision;
        axis.CancelGesture();
    }

    /// <summary>将验证失败的详情输入重新聚焦。</summary>
    public void FocusInvalidField(string? fieldKey)
    {
        fieldKey ??= coordinator.InvalidFieldKey;
        var parts = fieldKey?.Split('.') ?? [];
        if (parts.Length > 1 && parts[0] is "Selection" or "Highlight")
        {
            editingHighlight = parts[0] == "Highlight";
            synchronizing = true;
            activeTab = tabs.SelectedIndex = 0;
            synchronizing = false;
            Refresh();
            var name = parts[1] switch
            {
                nameof(SubtitleDetailsStyleDraft.FontFamily) => "SelectionFontInput",
                nameof(SubtitleDetailsStyleDraft.FontSizeText) => "SelectionFontSizeInput",
                nameof(SubtitleDetailsStyleDraft.Fill) => "SelectionFillInput",
                nameof(SubtitleDetailsStyleDraft.Stroke) => "SelectionStrokeInput",
                nameof(SubtitleDetailsStyleDraft.StrokeWidthText) => "SelectionStrokeWidthInput",
                nameof(SubtitleDetailsStyleDraft.Shadow) => "SelectionShadowInput",
                nameof(SubtitleDetailsStyleDraft.ShadowXText) => "SelectionShadowXInput",
                nameof(SubtitleDetailsStyleDraft.ShadowYText) => "SelectionShadowYInput",
                nameof(SubtitleDetailsStyleDraft.ShadowBlurText) => "SelectionShadowBlurInput",
                _ => null
            };
            var input = styleFields.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.Name == name);
            if (input is not null)
            {
                FocusInput(input, parts.Length > 2 ? parts[2] : null);
                return;
            }
        }
        if (fieldKey == "Code" || tabs.SelectedIndex == 1)
        {
            code.Focus();
        }
        else if (fieldKey == "Duration")
        {
            if (!clipPopup.IsOpen)
            {
                clipPopup.ShowAt(axis);
            }
            FocusInput(duration);
        }
        else
        {
            rich.Focus();
        }
    }

    private static void FocusInput(Control input, string? colorField = null)
    {
        input.BringIntoView();
        if (input is ColorDraftInput color && color.TryFocusInvalidField(colorField))
        {
            return;
        }
        var text = input.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
        if (text is not null)
        {
            text.Focus();
        }
        else
        {
            input.Focus();
        }
    }

    private bool HasFocusWithin()
    {
        if (hostWindow is not { IsActive: true })
        {
            return false;
        }
        var focused = hostWindow.FocusManager.GetFocusedElement() as Visual;
        return focused is not null && (ReferenceEquals(focused, this) || focused.GetVisualAncestors().Contains(this) ||
            clipPopup.IsOpen && clipPopup.Child is { } popupContent &&
            (ReferenceEquals(focused, popupContent) || focused.GetVisualAncestors().Contains(popupContent))) ||
            this.GetVisualDescendants().OfType<Button>().Any(button => button.Flyout?.IsOpen == true) ||
            this.GetVisualDescendants().OfType<FontFamilyPicker>().Any(picker => picker.IsDropDownOpen) ||
            this.GetVisualDescendants().OfType<ComboBox>().Any(picker => picker.IsDropDownOpen);
    }

    private void OnPanelGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (coordinator.IsPlaying != lastPlaybackState)
        {
            Refresh();
        }
    }

    private void OnPanelLostFocus(object? sender, RoutedEventArgs e)
    {
        var revision = ++focusRevision;
        var root = hostWindow;
        Dispatcher.UIThread.Post(() =>
        {
            if (!disposed && revision == focusRevision && ReferenceEquals(root, hostWindow) && !HasFocusWithin())
            {
                _ = session.RunCommandAsync(coordinator.StopPlaybackAsync);
            }
        }, DispatcherPriority.Background);
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        hostWindow = TopLevel.GetTopLevel(this) as Window;
        if (hostWindow is not null)
        {
            hostWindow.Activated += OnHostActivated;
            hostWindow.Deactivated += OnHostDeactivated;
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ++focusRevision;
        ++styleFocusRevision;
        if (hostWindow is not null)
        {
            hostWindow.Activated -= OnHostActivated;
            hostWindow.Deactivated -= OnHostDeactivated;
            hostWindow = null;
        }
        axis.CancelGesture();
        clipPopup.ForceClose();
        _ = session.RunCommandAsync(coordinator.StopPlaybackAsync);
        base.OnDetachedFromVisualTree(e);
    }

    private void OnHostDeactivated(object? sender, EventArgs e) => _ = session.RunCommandAsync(coordinator.StopPlaybackAsync);

    private void OnHostActivated(object? sender, EventArgs e)
    {
        if (!disposed && coordinator.InvalidFieldKey is { } field)
        {
            var root = hostWindow;
            var targetId = coordinator.Line?.Id;
            Dispatcher.UIThread.Post(() =>
            {
                if (!disposed && ReferenceEquals(root, hostWindow) && root?.IsActive == true &&
                    coordinator.Line?.Id == targetId && coordinator.InvalidFieldKey == field)
                {
                    FocusInvalidField(field);
                }
            }, DispatcherPriority.Background);
        }
    }

    /// <summary>释放详情输入、共享排版缓存及本地订阅。</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        if (hostWindow is not null)
        {
            hostWindow.Activated -= OnHostActivated;
            hostWindow.Deactivated -= OnHostDeactivated;
        }
        _ = session.RunCommandAsync(coordinator.StopPlaybackAsync);
        coordinator.Changed -= OnCoordinatorChanged;
        session.StyleLibraryChanged -= OnStyleLibraryChanged;
        session.ViewModel.GesturesCancelled -= OnGesturesCancelled;
        session.PreviewUpdated -= OnPreviewUpdated;
        Localization.LanguageChanged -= OnLanguageChanged;
        axis.CancelGesture();
        clipPopup.ForceClose();
        rich.Dispose();
        foreach (var binding in bindings)
        {
            binding.Dispose();
        }
        bindings.Clear();
    }
}
