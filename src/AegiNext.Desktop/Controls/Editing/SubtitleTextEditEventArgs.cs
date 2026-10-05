namespace AegiNext.Desktop.Controls;

/// <summary>富文本控件提交的字素安全文字替换；业务事务由消费方负责。</summary>
public sealed class SubtitleTextEditEventArgs(int start, int length, string replacement) : EventArgs
{
    public int Start { get; } = start;
    public int Length { get; } = length;
    public string Replacement { get; } = replacement;
}
