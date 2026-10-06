namespace AegiNext.Desktop.Controls;

/// <summary>宿主提供的字体候选及家族别名；控件不读取平台字体或全局服务。</summary>
public sealed record FontPickerCandidate
{
    /// <summary>复制候选的别名，避免宿主刷新集合改变当前搜索快照。</summary>
    public FontPickerCandidate(FontSelection selection, IEnumerable<string>? aliases = null)
    {
        Selection = selection;
        Aliases = Array.AsReadOnly((aliases ?? []).Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public FontSelection Selection { get; }
    public IReadOnlyList<string> Aliases { get; }
    public string DisplayName => Selection.DisplayName;

    /// <inheritdoc />
    public override string ToString() => DisplayName;
}
