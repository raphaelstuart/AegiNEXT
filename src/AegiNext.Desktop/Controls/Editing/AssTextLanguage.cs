namespace AegiNext.Desktop.Controls;

internal static class AssTextLanguage
{
    private static readonly string[] tagNames = ["iclip", "alpha", "xbord", "ybord", "xshad", "yshad", "fscx", "fscy", "bord", "shad", "blur", "move", "clip", "fade", "pos", "fad", "frz", "frx", "fry", "fsp", "org", "pbo", "fn", "fs", "an", "kf", "ko", "kt", "1c", "2c", "3c", "4c", "1a", "2a", "3a", "4a", "b", "i", "u", "s", "c", "r", "k", "K", "p", "q", "t"];

    internal static IReadOnlyList<SyntaxToken> Tokenize(string source)
    {
        var result = new List<SyntaxToken>();
        var inBlock = false;
        var index = 0;
        while (index < source.Length)
        {
            var start = index;
            var kind = SyntaxTokenKind.TEXT;
            if (!inBlock)
            {
                if (IsTextEscape(source, index))
                {
                    index += 2;
                    kind = SyntaxTokenKind.FUNCTION;
                }
                else if (source[index] == '{')
                {
                    inBlock = true;
                    index++;
                    kind = SyntaxTokenKind.PROPERTY;
                }
                else
                {
                    do
                    {
                        index++;
                    }
                    while (index < source.Length && source[index] != '{' && !IsTextEscape(source, index));
                }
            }
            else if (source[index] == '}')
            {
                inBlock = false;
                index++;
                kind = SyntaxTokenKind.PROPERTY;
            }
            else if (source[index] == '\\')
            {
                index++;
                var name = tagNames.FirstOrDefault(tag => source.AsSpan(index).StartsWith(tag, StringComparison.Ordinal));
                if (name is not null)
                {
                    index += name.Length;
                }
                else
                {
                    while (index < source.Length && char.IsAsciiLetterOrDigit(source[index]))
                    {
                        index++;
                    }
                }

                result.Add(new(start, index - start, SyntaxTokenKind.KEYWORD));
                if (name is "fn" or "r")
                {
                    start = index;
                    while (index < source.Length && source[index] is not ('\\' or '}'))
                    {
                        index++;
                    }

                    if (index > start)
                    {
                        result.Add(new(start, index - start, SyntaxTokenKind.STRING));
                    }
                }

                continue;
            }
            else if (source[index] == '&' && index + 1 < source.Length && source[index + 1] is 'H' or 'h')
            {
                index += 2;
                while (index < source.Length && char.IsAsciiHexDigit(source[index]))
                {
                    index++;
                }

                if (index < source.Length && source[index] == '&')
                {
                    index++;
                }

                kind = SyntaxTokenKind.STRING;
            }
            else if (IsNumberStart(source, index))
            {
                if (source[index] is '+' or '-')
                {
                    index++;
                }

                while (index < source.Length && char.IsAsciiDigit(source[index]))
                {
                    index++;
                }

                if (index < source.Length && source[index] == '.')
                {
                    index++;
                    while (index < source.Length && char.IsAsciiDigit(source[index]))
                    {
                        index++;
                    }
                }

                kind = SyntaxTokenKind.NUMBER;
            }
            else if (source[index] is '(' or ')' or ',')
            {
                index++;
                kind = SyntaxTokenKind.PROPERTY;
            }
            else
            {
                do
                {
                    index++;
                }
                while (index < source.Length && source[index] is not ('\\' or '}' or '(' or ')' or ',' or '&') && !IsNumberStart(source, index));
                kind = SyntaxTokenKind.COMMENT;
            }

            result.Add(new(start, index - start, kind));
        }

        return result;
    }

    private static bool IsTextEscape(string source, int index) => source[index] == '\\' &&
        index + 1 < source.Length && source[index + 1] is 'N' or 'n' or 'h' or '{' or '}';

    private static bool IsNumberStart(string source, int index)
    {
        if (char.IsAsciiDigit(source[index]))
        {
            return true;
        }

        if (source[index] is '+' or '-')
        {
            index++;
        }

        return index < source.Length && (char.IsAsciiDigit(source[index]) ||
            source[index] == '.' && index + 1 < source.Length && char.IsAsciiDigit(source[index + 1]));
    }
}
