namespace AegiNext.Desktop.Controls;

/// <summary>使用原生文本布局着色字幕 ASS 标签及未完成的输入草稿。</summary>
public sealed class AssTextPresenter : SyntaxTextPresenter
{
    private protected override IReadOnlyList<SyntaxToken> Tokenize(string source) => AssTextLanguage.Tokenize(source);
}
