namespace AegiNext.Desktop.Controls;

/// <summary>用户确认选择或输入的字体族名称。</summary>
public sealed class FontFamilyCommittedEventArgs(string familyName) : EventArgs
{
    public string FamilyName { get; } = familyName;
}
