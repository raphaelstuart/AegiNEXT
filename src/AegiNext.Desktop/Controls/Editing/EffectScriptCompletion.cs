namespace AegiNext.Desktop.Controls;

/// <summary>纯文本补全项；替换范围相对于编辑器源文件。</summary>
public sealed record EffectScriptCompletion(int Start, int Length, string Insertion, string Label, string Hint);
