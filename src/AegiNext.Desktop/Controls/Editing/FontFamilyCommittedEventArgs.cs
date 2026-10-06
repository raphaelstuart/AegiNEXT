namespace AegiNext.Desktop.Controls;

/// <summary>用户确认的完整字体身份，保留旧家族事件接口。</summary>
public sealed class FontFamilyCommittedEventArgs : EventArgs
{
    /// <summary>创建普通家族提交。</summary>
    public FontFamilyCommittedEventArgs(string familyName) : this(new FontSelection(familyName))
    {
    }

    /// <summary>创建家族或系统命名变体提交。</summary>
    public FontFamilyCommittedEventArgs(FontSelection selection)
    {
        Selection = selection;
    }

    public FontSelection Selection { get; }
    public string FamilyName => Selection.FamilyName;
    public AegiNext.Core.Projects.SubtitleFontVariant? Variant => Selection.Variant;
    public bool IsSystemFont => Selection.IsSystemFont;
}
