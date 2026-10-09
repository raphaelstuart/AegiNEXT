using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Styling;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Utils;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace AegiNext.Desktop.Controls;

/// <summary>共享的可搜索字体下拉框；搜索草稿与已确认字体分离。</summary>
public sealed class FontFamilyPicker : AutoCompleteBox
{
    /// <summary>菜单名称的异步预览提供方，由应用宿主注入。</summary>
    public static readonly StyledProperty<IFontNamePreviewProvider?> PreviewProviderProperty =
        AvaloniaProperty.Register<FontFamilyPicker, IFontNamePreviewProvider?>(nameof(PreviewProvider));

    private FontPickerCandidate[] systemCandidates = [];
    private string[] additionalFamilies = [];
    private FontSelection committedFont = new(string.Empty);
    private bool showAllFamilies;
    private bool synchronizing;
    private bool dropDownOpened;
    private ISelectionAdapter? hookedAdapter;
    private FontFamilyMenu? fontMenu;
    private TextBox? input;

    /// <summary>沿用 Avalonia 输入、下拉导航及无障碍模板；目录由宿主提供。</summary>
    public FontFamilyPicker()
    {
        MinimumPrefixLength = 0;
        MinimumPopulateDelay = TimeSpan.Zero;
        IsTextCompletionEnabled = false;
        MaxDropDownHeight = 280;
        MaxLength = 512;
        ItemFilter = (query, item) => showAllFamilies || (item switch
        {
            FontPickerCandidate candidate => FontSelectionResolver.MatchesQuery(candidate, query ?? string.Empty),
            string family => family.Contains(query ?? string.Empty, StringComparison.OrdinalIgnoreCase),
            _ => false
        });
        var toggle = new Button
        {
            Name = "FontDropDownButton", Classes = { "icon-button" },
            Focusable = false, Background = Brushes.Transparent, BorderThickness = new(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = WorkbenchIcon.Create("Down", 12)
        };
        toggle.Bind(AutomationProperties.NameProperty, Localization.Observe("Workbench.Font").ToBinding());
        toggle.Click += (_, _) =>
        {
            if (IsDropDownOpen)
            {
                SetCurrentValue(IsDropDownOpenProperty, false);
            }
            else
            {
                OpenFontList();
            }
        };
        InnerRightContent = toggle;
        RefreshFontFamilies();
    }

    public event EventHandler<FontFamilyCommittedEventArgs>? FamilyCommitted;
    public IReadOnlyList<string> FontFamilies { get; private set; } = [];
    public IReadOnlyList<FontPickerCandidate> FontCandidates { get; private set; } = [];
    public FontSelection CurrentFont => committedFont;
    public bool CommitOnLostFocus { get; set; } = true;

    /// <summary>取得或设置字体名称预览；未提供时使用普通菜单文字。</summary>
    public IFontNamePreviewProvider? PreviewProvider
    {
        get => GetValue(PreviewProviderProperty);
        set => SetValue(PreviewProviderProperty, value);
    }

    /// <summary>宿主接管输入完成命令时，可关闭未展开列表的本地 Esc 恢复。</summary>
    public bool RestoreOnEscape { get; set; } = true;

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(AutoCompleteBox);

    /// <summary>追加工程或预设字体，保留尚未提交的输入文本。</summary>
    public void RefreshFontFamilies(IEnumerable<string>? additionalFamilies = null)
    {
        this.additionalFamilies = (additionalFamilies ?? []).ToArray();
        RefreshCandidates();
    }

    /// <summary>替换宿主提供的系统字体目录，并保留搜索草稿。</summary>
    public void RefreshFontCandidates(IEnumerable<FontPickerCandidate> candidates, IEnumerable<string>? additionalFamilies = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        systemCandidates = FontSelectionResolver.NormalizeCandidates(candidates).ToArray();
        if (additionalFamilies is not null)
        {
            this.additionalFamilies = additionalFamilies.ToArray();
        }
        RefreshCandidates();
    }

    private void RefreshCandidates()
    {
        var text = input?.Text ?? Text ?? string.Empty;
        var systemFamilies = systemCandidates.Select(value => value.Selection.FamilyName)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var families = systemFamilies.Concat(additionalFamilies).Append(committedFont.FamilyName)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
        var familyCandidates = families.Select(family => new FontPickerCandidate(
            new(family, isSystemFont: systemFamilies.Contains(family, StringComparer.OrdinalIgnoreCase))));
        var candidates = systemCandidates.Concat(familyCandidates);
        if (!string.IsNullOrWhiteSpace(committedFont.FamilyName))
        {
            candidates = candidates.Append(new(committedFont));
        }
        var values = candidates.DistinctBy(value => FontSelectionResolver.GetFaceKey(value.Selection))
            .OrderBy(value => value.Selection.FamilyName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(value => value.Selection.Variant?.Weight ?? 0)
            .ThenBy(value => value.Selection.Variant?.Width ?? 0)
            .ThenBy(value => value.Selection.Variant?.Italic ?? false)
            .ThenBy(value => value.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
        synchronizing = true;
        try
        {
            FontFamilies = Array.AsReadOnly(families);
            FontCandidates = Array.AsReadOnly(values);
            ItemsSource = values;
            SetCurrentValue(TextProperty, text);
        }
        finally
        {
            synchronizing = false;
        }
    }

    /// <summary>回填模型当前字体，不产生用户提交事件。</summary>
    public void SetCurrentFamily(string familyName)
    {
        SetCurrentFont(new(familyName));
    }

    /// <summary>回填完整字体身份，不产生提交或重新解释不可用的已存变体。</summary>
    public void SetCurrentFont(FontSelection selection)
    {
        var changed = committedFont != selection;
        synchronizing = true;
        try
        {
            committedFont = selection;
            SetCurrentValue(TextProperty, selection.DisplayName);
        }
        finally
        {
            synchronizing = false;
        }

        if (changed || !FontCandidates.Any(value => FontSelectionResolver.HasSameFace(value.Selection, selection)))
        {
            RefreshCandidates();
        }
    }

    /// <summary>供宿主解析草稿，查询与提交共用候选规则。</summary>
    public bool TryResolveSelection(string text, out FontSelection selection) =>
        FontSelectionResolver.TryResolve(FontCandidates, committedFont, text, out selection);

    /// <summary>展开全部候选字体，当前输入与已提交字体均保持不变。</summary>
    public void OpenFontList()
    {
        input?.Focus();
        showAllFamilies = true;
        if (IsDropDownOpen)
        {
            PopulateComplete();
        }
        else
        {
            SetCurrentValue(IsDropDownOpenProperty, true);
        }
    }

    /// <summary>确认当前文本；空输入不提交，已确认值不重复产生事件。</summary>
    public bool CommitText()
    {
        if (synchronizing)
        {
            return true;
        }

        if (!TryResolveSelection(input?.Text ?? Text ?? string.Empty, out var selection))
        {
            return false;
        }
        CommitSelection(selection);
        return true;
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        input?.RemoveHandler(KeyDownEvent, OnInputKeyDown);
        if (hookedAdapter is not null)
        {
            hookedAdapter.Commit -= OnSelectionCommitted;
            hookedAdapter.Cancel -= OnSelectionCancelled;
        }

        base.OnApplyTemplate(e);
        input = e.NameScope.Find<TextBox>("PART_TextBox");
        input?.AddHandler(KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);
        if (e.NameScope.Find<Popup>("PART_Popup") is { } popup)
        {
            popup.OverlayDismissEventPassThrough = true;
        }
        hookedAdapter = SelectionAdapter;
        if (hookedAdapter is not null)
        {
            hookedAdapter.Commit += OnSelectionCommitted;
            hookedAdapter.Cancel += OnSelectionCancelled;
        }
    }

    /// <inheritdoc />
    protected override ISelectionAdapter? GetSelectionAdapterPart(INameScope nameScope)
    {
        if (fontMenu is not null)
        {
            fontMenu.Closed -= OnFontMenuClosed;
            fontMenu.Release();
        }
        fontMenu = null;
        if (nameScope.Find<Popup>("PART_Popup") is not { } popup)
        {
            return base.GetSelectionAdapterPart(nameScope);
        }
        fontMenu = new(() => committedFont) { MaxHeight = MaxDropDownHeight, PreviewProvider = PreviewProvider };
        fontMenu.Closed += OnFontMenuClosed;
        popup.Child = fontMenu;
        return fontMenu;
    }

    /// <inheritdoc />
    protected override void OnDropDownOpened(EventArgs e)
    {
        fontMenu?.Open();
        if (!dropDownOpened)
        {
            dropDownOpened = true;
            base.OnDropDownOpened(e);
        }
    }

    /// <inheritdoc />
    protected override void OnDropDownClosed(EventArgs e)
    {
        if (!dropDownOpened)
        {
            return;
        }
        dropDownOpened = false;
        fontMenu?.Dismiss();
        base.OnDropDownClosed(e);
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SetCurrentValue(IsDropDownOpenProperty, false);
        fontMenu?.Dismiss();
        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PreviewProviderProperty && fontMenu is not null)
        {
            fontMenu.PreviewProvider = PreviewProvider;
        }
    }

    /// <inheritdoc />
    protected override void OnTextChanged(TextChangedEventArgs e)
    {
        showAllFamilies = false;
        base.OnTextChanged(e);
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }
        if (e.Key == Key.F4 || e.Key == Key.Down && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            OpenFontList();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (!RestoreOnEscape && !IsDropDownOpen)
            {
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
            SetCurrentFont(committedFont);
            SetCurrentValue(IsDropDownOpenProperty, false);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
        if (e.Key == Key.Enter && !e.Handled)
        {
            CommitText();
            SetCurrentValue(IsDropDownOpenProperty, false);
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        if (fontMenu?.ContainsKeyboardFocus == true)
        {
            e.Handled = true;
            return;
        }
        base.OnLostFocus(e);
        if (CommitOnLostFocus && !IsKeyboardFocusWithin)
        {
            CommitText();
        }
    }

    private void OnSelectionCancelled(object? sender, RoutedEventArgs e)
    {
        SetCurrentFont(committedFont);
        input?.Focus();
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (IsDropDownOpen && fontMenu is not null &&
            (e.Key is Key.Up or Key.Down or Key.Enter || e.Key == Key.Right && fontMenu.SelectedIndex >= 0))
        {
            fontMenu.HandleKeyDown(e);
        }
    }

    private void OnFontMenuClosed(object? sender, RoutedEventArgs e)
    {
        SetCurrentValue(IsDropDownOpenProperty, false);
    }

    private void OnSelectionCommitted(object? sender, RoutedEventArgs e)
    {
        if ((sender as FontFamilyMenu)?.CommittedCandidate is { } committedCandidate)
        {
            CommitSelection(committedCandidate.Selection);
        }
        else if (SelectedItem is FontPickerCandidate candidate)
        {
            CommitSelection(candidate.Selection);
        }
        else
        {
            CommitText();
        }
    }

    private void CommitSelection(FontSelection selection)
    {
        var changed = committedFont != selection;
        SetCurrentFont(selection);
        if (changed)
        {
            FamilyCommitted?.Invoke(this, new(selection));
        }
    }
}
