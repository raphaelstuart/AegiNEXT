using System.Globalization;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Views;

internal sealed class IntegerInputDialogViewModel : ObservableObject, IDisposable
{
    private readonly IntegerInputRequest request;
    private string rawText;
    private bool disposed;

    internal IntegerInputDialogViewModel(IntegerInputRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TitleKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LabelKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.HintKey);
        if (request.Minimum > request.Maximum || request.InitialValue < request.Minimum || request.InitialValue > request.Maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Integer input bounds and initial value must be valid.");
        }

        this.request = request;
        rawText = request.InitialValue.ToString(CultureInfo.CurrentCulture);
        ConfirmCommand = new(() => TryConfirm(), () => !disposed && Result is null);
        Localization.LanguageChanged += OnLanguageChanged;
    }

    internal event EventHandler? Confirmed;
    internal int? Result { get; private set; }

    /// <summary>确认有效的整数草稿。</summary>
    public RelayCommand ConfirmCommand { get; }

    /// <summary>输入窗口的本地化标题。</summary>
    public string Title => Localization.Get(request.TitleKey);

    /// <summary>整数输入的本地化字段名称。</summary>
    public string Label => Localization.Get(request.LabelKey);

    /// <summary>输入字段的本地化说明。</summary>
    public string Hint => Localization.Get(request.HintKey);

    /// <summary>允许输入的最小整数。</summary>
    public int Minimum => request.Minimum;

    /// <summary>允许输入的最大整数。</summary>
    public int Maximum => request.Maximum;

    /// <summary>保留未完成、越界和非法输入的原始文本。</summary>
    public string RawText
    {
        get => rawText;
        set
        {
            if (SetProperty(ref rawText, value ?? string.Empty))
            {
                RefreshValidation();
            }
        }
    }

    /// <summary>非法草稿的本地化整数范围说明。</summary>
    public string? Error => TryGetValue(out _) ? null : Localization.Format("Workbench.IntegerInputInvalid", Minimum, Maximum);

    /// <summary>当前草稿是否需要显示错误。</summary>
    public bool HasError => Error is not null;

    internal bool TryConfirm()
    {
        if (disposed || Result is not null || !TryGetValue(out var value))
        {
            RefreshValidation();
            return false;
        }

        Result = value;
        ConfirmCommand.NotifyCanExecuteChanged();
        Confirmed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private bool TryGetValue(out int value)
    {
        return int.TryParse(RawText, NumberStyles.Integer, CultureInfo.CurrentCulture, out value) &&
            value >= Minimum && value <= Maximum;
    }

    private void RefreshValidation()
    {
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(HasError));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Hint));
        RefreshValidation();
    }

    /// <summary>释放语言更新订阅并阻止再次确认。</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Localization.LanguageChanged -= OnLanguageChanged;
        ConfirmCommand.NotifyCanExecuteChanged();
    }
}
