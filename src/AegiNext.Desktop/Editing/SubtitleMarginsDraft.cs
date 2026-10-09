using System.ComponentModel;
using System.Globalization;
using AegiNext.Core.Projects;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Editing;

/// <summary>保留左右及垂直边距原文的纯草稿，不持有界面或工程事务。</summary>
public sealed class SubtitleMarginsDraft : ObservableObject
{
    private const double MAX_MARGIN = 32768;
    private SubtitleMargins source = new();
    private CultureInfo leftCulture = CultureInfo.CurrentCulture;
    private CultureInfo rightCulture = CultureInfo.CurrentCulture;
    private CultureInfo verticalCulture = CultureInfo.CurrentCulture;
    private bool loading;

    /// <summary>创建三个独立边距草稿并载入默认边距。</summary>
    public SubtitleMarginsDraft()
    {
        Left.PropertyChanged += FieldChanged;
        Right.PropertyChanged += FieldChanged;
        Vertical.PropertyChanged += FieldChanged;
        Load(new());
    }

    public NumericValueDraft Left { get; } = new() { PreserveDoublePrecision = true };
    public NumericValueDraft Right { get; } = new() { PreserveDoublePrecision = true };
    public NumericValueDraft Vertical { get; } = new() { PreserveDoublePrecision = true };
    public bool IsDirty => !TryRead(Left, leftCulture, out var left) || left != source.Left ||
                           !TryRead(Right, rightCulture, out var right) || right != source.Right ||
                           !TryRead(Vertical, verticalCulture, out var vertical) || vertical != source.Vertical;
    public SubtitleMargins? DiagramMargins => Validate() is null ? CreateMargins() : null;
    public event EventHandler? Changed;

    /// <summary>载入已验证边距，更新恢复快照而不产生用户编辑事件。</summary>
    public void Load(SubtitleMargins margins, CultureInfo? culture = null)
    {
        ValidateSource(margins.Left);
        ValidateSource(margins.Right);
        ValidateSource(margins.Vertical);
        loading = true;
        try
        {
            source = margins;
            leftCulture = rightCulture = verticalCulture = culture ?? CultureInfo.CurrentCulture;
            LoadValue(Left, margins.Left, leftCulture);
            LoadValue(Right, margins.Right, rightCulture);
            LoadValue(Vertical, margins.Vertical, verticalCulture);
        }
        finally
        {
            loading = false;
        }
        NotifyState();
    }

    /// <summary>返回首个无效字段，允许原文继续编辑而不退回上次有效投影。</summary>
    public string? Validate()
    {
        if (!TryRead(Left, leftCulture, out _))
        {
            return "MarginLeftInput";
        }
        if (!TryRead(Right, rightCulture, out _))
        {
            return "MarginRightInput";
        }
        if (!TryRead(Vertical, verticalCulture, out _))
        {
            return "MarginVerticalInput";
        }
        return null;
    }

    /// <summary>从已验证原文创建三轴边距，保留双精度来源及用户输入的精度。</summary>
    public SubtitleMargins CreateMargins()
    {
        if (!TryRead(Left, leftCulture, out var left) ||
            !TryRead(Right, rightCulture, out var right) ||
            !TryRead(Vertical, verticalCulture, out var vertical))
        {
            throw new InvalidDataException("字幕边距草稿无效。");
        }
        return new(left, right, vertical);
    }

    /// <summary>仅恢复指定字段最近一次载入的值，保留其他字段的未完成输入。</summary>
    public bool RestoreField(string key)
    {
        return key switch
        {
            "MarginLeftInput" => LoadField(key, source.Left, leftCulture),
            "MarginRightInput" => LoadField(key, source.Right, rightCulture),
            "MarginVerticalInput" => LoadField(key, source.Vertical, verticalCulture),
            _ => false
        };
    }

    /// <summary>替换一个字段及其恢复快照，供宿主按当前已提交字幕恢复边距。</summary>
    public bool LoadField(string key, double original, CultureInfo? culture = null)
    {
        var field = key switch
        {
            "MarginLeftInput" => Left,
            "MarginRightInput" => Right,
            "MarginVerticalInput" => Vertical,
            _ => null
        };
        if (field is null)
        {
            return false;
        }
        ValidateSource(original);
        var formatCulture = culture ?? CultureInfo.CurrentCulture;
        loading = true;
        try
        {
            switch (key)
            {
                case "MarginLeftInput":
                    source = source with { Left = original };
                    leftCulture = formatCulture;
                    break;
                case "MarginRightInput":
                    source = source with { Right = original };
                    rightCulture = formatCulture;
                    break;
                case "MarginVerticalInput":
                    source = source with { Vertical = original };
                    verticalCulture = formatCulture;
                    break;
            }
            LoadValue(field, original, formatCulture);
        }
        finally
        {
            loading = false;
        }
        RaiseChanged();
        return true;
    }

    private static void LoadValue(NumericValueDraft field, double original, CultureInfo culture)
    {
        field.Value = (decimal)original;
        field.RawText = original.ToString("R", culture);
    }

    private static bool TryRead(NumericValueDraft field, CultureInfo culture, out double value)
    {
        return double.TryParse(field.RawText, NumberStyles.Float, culture, out value) &&
               double.IsFinite(value) && value is >= 0 and <= MAX_MARGIN;
    }

    private static void ValidateSource(double value)
    {
        if (!double.IsFinite(value) || value is < 0 or > MAX_MARGIN)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    private void FieldChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!loading && args.PropertyName == nameof(NumericValueDraft.RawText))
        {
            RaiseChanged();
        }
    }

    private void RaiseChanged()
    {
        NotifyState();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(DiagramMargins));
    }
}
