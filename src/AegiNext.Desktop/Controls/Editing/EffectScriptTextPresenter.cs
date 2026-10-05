namespace AegiNext.Desktop.Controls;

/// <summary>为特效脚本提供词法颜色，保留共享原生文本输入与布局行为。</summary>
public sealed class EffectScriptTextPresenter : SyntaxTextPresenter
{
    private protected override IReadOnlyList<SyntaxToken> Tokenize(string source) => EffectScriptLanguage.Tokenize(source);
}
