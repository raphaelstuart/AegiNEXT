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
    private FontPickerCandidate[] systemCandidates = [];
    private string[] additionalFamilies = [];
    private FontSelection committedFont = new(string.Empty);
    private bool showAllFamilies;
    private bool synchronizing;
    private ISelectionAdapter? hookedAdapter;
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
        var values = candidates.DistinctBy(value => (value.Selection.FamilyName.ToUpperInvariant(), value.Selection.Variant))
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

        if (!FontCandidates.Any(value => value.Selection.FamilyName == selection.FamilyName && value.Selection.Variant == selection.Variant))
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
        if (hookedAdapter is not null)
        {
            hookedAdapter.Commit -= OnSelectionCommitted;
        }

        base.OnApplyTemplate(e);
        input = e.NameScope.Find<TextBox>("PART_TextBox");
        if (e.NameScope.Find<Popup>("PART_Popup") is { } popup)
        {
            popup.OverlayDismissEventPassThrough = true;
        }
        hookedAdapter = SelectionAdapter;
        if (hookedAdapter is not null)
        {
            hookedAdapter.Commit += OnSelectionCommitted;
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
        if (e.Key == Key.Enter)
        {
            CommitText();
            SetCurrentValue(IsDropDownOpenProperty, false);
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        if (CommitOnLostFocus && !IsKeyboardFocusWithin)
        {
            CommitText();
        }
    }

    private void OnSelectionCommitted(object? sender, RoutedEventArgs e)
    {
        if (SelectedItem is FontPickerCandidate candidate)
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
        SetCurrentValue(TextProperty, selection.DisplayName);
        if (committedFont != selection)
        {
            committedFont = selection;
            FamilyCommitted?.Invoke(this, new(selection));
        }
    }
}
