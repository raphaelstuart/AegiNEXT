using Avalonia.Interactivity;

namespace AegiNext.Desktop.Controls;

/// <summary>描述数值标题手势的原文和输入身份，提交事务由消费者承担。</summary>
public sealed class NumericDragEventArgs : RoutedEventArgs
{
    /// <summary>创建数值标题手势通知。</summary>
    public NumericDragEventArgs(RoutedEvent routedEvent, NumericDraftInput input, string originalText, string finalText)
        : base(routedEvent)
    {
        Input = input;
        OriginalText = originalText;
        FinalText = finalText;
    }

    /// <summary>取得手势开始时冻结的输入控件。</summary>
    public NumericDraftInput Input { get; }

    /// <summary>取得手势开始时的原始草稿。</summary>
    public string OriginalText { get; }

    /// <summary>取得通知时的原始草稿。</summary>
    public string FinalText { get; }

    /// <summary>取得原始草稿是否已经变化。</summary>
    public bool Changed => OriginalText != FinalText;
}
