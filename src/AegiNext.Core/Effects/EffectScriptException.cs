namespace AegiNext.Core.Effects;

/// <summary>带源文件行列位置的脚本语法或语义错误。</summary>
public sealed class EffectScriptException : FormatException
{
    /// <summary>记录诊断；行号为零表示通过数据接口构造的脚本没有源位置。</summary>
    public EffectScriptException(string message, int line = 0, int column = 1, Exception? innerException = null)
        : base(line > 0 ? $"第 {line} 行，第 {column} 列：{message}" : message, innerException)
    {
        Line = line;
        Column = column;
    }

    public int Line { get; }
    public int Column { get; }
}
