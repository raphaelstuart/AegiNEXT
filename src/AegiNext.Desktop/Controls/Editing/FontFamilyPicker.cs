using AegiNext.Desktop.Localization;
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
    private readonly string[] systemFamilies;
    private string committedFamily = string.Empty;
    private bool showAllFamilies;
    private bool synchronizing;
    private ISelectionAdapter? hookedAdapter;
    private TextBox? input;

    /// <summary>读取当前平台实际字体；沿用 Avalonia 输入、下拉导航及无障碍模板。</summary>
    public FontFamilyPicker()
    {
        systemFamilies = FontManager.Current.SystemFonts.Select(value => value.Name)
            .Append(FontManager.Current.DefaultFontFamily.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
        MinimumPrefixLength = 0;
        MinimumPopulateDelay = TimeSpan.Zero;
        IsTextCompletionEnabled = false;
        MaxDropDownHeight = 280;
        MaxLength = 512;
        ItemFilter = (query, item) => item is string family &&
            (showAllFamilies || family.Contains(query ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        var toggle = new Button
        {
            Name = "FontDropDownButton", Width = 28, MinHeight = 0, Padding = new(6, 0),
            Focusable = false, Background = Brushes.Transparent, BorderThickness = new(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = new PathIcon { Width = 12, Height = 12, Data = Geometry.Parse("M2 4L6 8L10 4L11 5L6 10L1 5Z") }
        };
        AutomationProperties.SetName(toggle, WorkbenchText.Get("Font"));
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
    public bool CommitOnLostFocus { get; set; } = true;

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(AutoCompleteBox);

    /// <summary>追加工程或预设字体，保留尚未提交的输入文本。</summary>
    public void RefreshFontFamilies(IEnumerable<string>? additionalFamilies = null)
    {
        var text = input?.Text ?? Text ?? string.Empty;
        var families = systemFamilies.Concat(additionalFamilies ?? []).Append(committedFamily)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
        synchronizing = true;
        try
        {
            FontFamilies = Array.AsReadOnly(families);
            ItemsSource = families;
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
        ArgumentNullException.ThrowIfNull(familyName);
        synchronizing = true;
        try
        {
            committedFamily = familyName;
            SetCurrentValue(TextProperty, familyName);
        }
        finally
        {
            synchronizing = false;
        }

        if (!FontFamilies.Contains(familyName, StringComparer.OrdinalIgnoreCase))
        {
            RefreshFontFamilies(FontFamilies.Append(familyName));
        }
    }

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

        var family = (input?.Text ?? Text ?? string.Empty).Trim();
        if (family.Length == 0 || family.Length > 512 || family.Any(char.IsControl))
        {
            return false;
        }

        SetCurrentValue(TextProperty, family);
        if (!string.Equals(committedFamily, family, StringComparison.Ordinal))
        {
            committedFamily = family;
            FamilyCommitted?.Invoke(this, new(family));
        }

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
            base.OnKeyDown(e);
            SetCurrentFamily(committedFamily);
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
        CommitText();
    }
}
