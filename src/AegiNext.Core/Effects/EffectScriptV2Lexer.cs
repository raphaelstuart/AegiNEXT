using System.Text;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Effects;

internal static class EffectScriptV2Lexer
{
    internal static string StripComment(string text, int line)
    {
        var quoted = false;
        var escaped = false;
        var opening = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (quoted)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    quoted = false;
                }
            }
            else if (character == '#')
            {
                return text[..index];
            }
            else if (character == '"')
            {
                quoted = true;
                opening = index;
            }
        }
        if (quoted)
        {
            throw new EffectScriptException("字符串必须在同一源代码行闭合；换行请使用转义。", line, opening + 1);
        }
        return text;
    }

    internal static string ReadString(string text, ref int offset, int line, int column)
    {
        var start = offset;
        if (offset >= text.Length || text[offset] != '"')
        {
            throw new EffectScriptException("split 分隔符必须为双引号字符串。", line, column + offset);
        }
        var value = new StringBuilder();
        for (offset++; offset < text.Length; offset++)
        {
            var character = text[offset];
            if (character == '\\')
            {
                if (++offset >= text.Length)
                {
                    break;
                }
                value.Append(text[offset] switch
                {
                    '"' => '"',
                    '\\' => '\\',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => throw new EffectScriptException("字符串仅支持双引号、反斜杠、换行、回车和制表符转义。", line, column + offset)
                });
            }
            else if (character == '"')
            {
                offset++;
                try
                {
                    var decoded = value.ToString();
                    ProjectValidator.ValidateText(decoded);
                    if (decoded.Length == 0)
                    {
                        throw new EffectScriptException("split 分隔符不能为空。", line, column + start);
                    }
                    return decoded;
                }
                catch (InvalidDataException error)
                {
                    throw new EffectScriptException("字符串转义或 Unicode 无效。", line, column + start, error);
                }
            }
            else if (character < ' ')
            {
                throw new EffectScriptException("字符串中的控制字符必须使用受支持的转义。", line, column + offset);
            }
            else
            {
                value.Append(character);
            }
        }
        throw new EffectScriptException("字符串缺少闭合双引号。", line, column + start);
    }

    internal static void SkipWhitespace(string text, ref int offset)
    {
        while (offset < text.Length && char.IsWhiteSpace(text[offset]))
        {
            offset++;
        }
    }
}
