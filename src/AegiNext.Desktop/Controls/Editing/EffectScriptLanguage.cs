using System.Text.RegularExpressions;
using AegiNext.Desktop.Localization;

namespace AegiNext.Desktop.Controls;

internal static class EffectScriptLanguage
{
    private static readonly Regex tokens = new("#[^\\r\\n]*|\"[^\"\\r\\n]*\"?|[+-]?(?:\\d+(?:\\.\\d+)?|\\.\\d+)(?:ms|s)?|[a-z][a-z-]*|\\s+|.",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly HashSet<string> keywords = ["effect", "version", "short-clip", "compress", "reject", "segment", "fixed", "flex", "at", "end"];
    private static readonly string[] properties = ["position", "scale", "rotation", "opacity", "blur", "stroke-width", "path-progress", "fill", "stroke"];
    private static readonly string[] interpolation = ["linear", "hold", "ease-in", "ease-out", "ease-in-out"];

    internal static IReadOnlyList<EffectScriptToken> Tokenize(string source)
    {
        var result = new List<EffectScriptToken>();
        foreach (Match match in tokens.Matches(source))
        {
            var value = match.Value;
            var kind = value[0] switch
            {
                '#' => EffectScriptTokenKind.COMMENT,
                '"' => EffectScriptTokenKind.STRING,
                _ when keywords.Contains(value) => EffectScriptTokenKind.KEYWORD,
                _ when properties.Contains(value, StringComparer.Ordinal) => EffectScriptTokenKind.PROPERTY,
                _ when value is "base" or "offset" or "factor" or "rgba" => EffectScriptTokenKind.FUNCTION,
                _ when interpolation.Contains(value, StringComparer.Ordinal) => EffectScriptTokenKind.INTERPOLATION,
                _ when char.IsDigit(value[0]) || value[0] is '+' or '-' or '.' && value.Length > 1 => EffectScriptTokenKind.NUMBER,
                _ => EffectScriptTokenKind.TEXT
            };
            result.Add(new(match.Index, match.Length, kind));
        }

        return result;
    }

    internal static IReadOnlyList<EffectScriptCompletion> Complete(string source, int caret)
    {
        caret = Math.Clamp(caret, 0, source.Length);
        var lineStart = source.LastIndexOf('\n', Math.Max(0, caret - 1), Math.Min(caret, source.Length));
        lineStart++;
        var prefix = source[lineStart..caret];
        if (prefix.Contains('#', StringComparison.Ordinal) || prefix.Count(character => character == '"') % 2 != 0)
        {
            return [];
        }

        var replacementStart = caret;
        while (replacementStart > lineStart && (char.IsLetter(source[replacementStart - 1]) || source[replacementStart - 1] == '-'))
        {
            replacementStart--;
        }

        var partial = source[replacementStart..caret];
        var before = source[lineStart..replacementStart].Trim();
        var words = before.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var candidates = new List<(string Text, string Hint)>();
        if (words.Length == 0)
        {
            if (string.IsNullOrWhiteSpace(source[..lineStart]))
            {
                candidates.Add(("effect \"my-effect\" version 1", SettingsText.Get("ScriptHintHeader")));
            }
            else
            {
                var prior = source[..lineStart].Split('\n').Select(line => line.Split('#', 2)[0].Trim()).Where(line => line.Length > 0).ToArray();
                var inside = prior.Count(line => line.StartsWith("segment ", StringComparison.Ordinal)) > prior.Count(line => line == "end");
                if (inside)
                {
                    candidates.Add(("at 0 ", SettingsText.Get("ScriptHintAt")));
                    candidates.Add(("at 1 ", SettingsText.Get("ScriptHintEndpoints")));
                    candidates.Add(("end", SettingsText.Get("ScriptHintEnd")));
                }
                else if (!prior.Any(line => line.StartsWith("short-clip ", StringComparison.Ordinal)))
                {
                    candidates.Add(("short-clip compress", SettingsText.Get("ScriptHintCompress")));
                    candidates.Add(("short-clip reject", SettingsText.Get("ScriptHintReject")));
                }
                else
                {
                    candidates.Add(("segment enter fixed 300ms", SettingsText.Get("ScriptHintFixed")));
                    candidates.Add(("segment stay flex 1", SettingsText.Get("ScriptHintFlex")));
                }
            }
        }
        else if (words[0] == "short-clip")
        {
            candidates.Add(("compress", SettingsText.Get("ScriptHintCompress")));
            candidates.Add(("reject", SettingsText.Get("ScriptHintReject")));
        }
        else if (words[0] == "segment" && words.Length == 2)
        {
            candidates.Add(("fixed 300ms", SettingsText.Get("ScriptHintFixed")));
            candidates.Add(("flex 1", SettingsText.Get("ScriptHintFlex")));
        }
        else if (words[0] == "at" && words.Length == 2)
        {
            candidates.AddRange(properties.Select(value => (value, SettingsText.Get(value switch
            {
                "position" or "scale" => "ScriptHintVector",
                "fill" or "stroke" => "ScriptHintColor",
                _ => "ScriptHintScalar"
            }))));
        }
        else if (words[0] == "at" && words.Length == 3)
        {
            var vector = words[2] is "position" or "scale";
            if (words[2] is "fill" or "stroke")
            {
                candidates.Add(("base", SettingsText.Get("ScriptHintBase")));
                candidates.Add(("rgba(1, 1, 1, 1)", SettingsText.Get("ScriptHintColor")));
            }
            else if (words[2] == "path-progress")
            {
                candidates.Add(("0", SettingsText.Get("ScriptHintPath")));
                candidates.Add(("1", SettingsText.Get("ScriptHintPath")));
            }
            else
            {
                candidates.Add(("base", SettingsText.Get("ScriptHintBase")));
                candidates.Add((vector ? "offset(0, 0)" : "offset(0)", SettingsText.Get("ScriptHintOffset")));
                candidates.Add((vector ? "factor(1, 1)" : "factor(1)", SettingsText.Get("ScriptHintFactor")));
                candidates.Add((vector ? "(0, 0)" : "0", SettingsText.Get("ScriptHintAbsolute")));
            }
        }
        else if (words[0] == "at" && words.Length >= 4 && (!before.Contains('(', StringComparison.Ordinal) || before.Contains(')', StringComparison.Ordinal)))
        {
            candidates.AddRange(interpolation.Select(value => (value, SettingsText.Get("ScriptHintInterpolation"))));
        }

        return candidates.Where(item => item.Text.StartsWith(partial, StringComparison.Ordinal))
            .Select(item => new EffectScriptCompletion(replacementStart, caret - replacementStart, item.Text, item.Text, item.Hint)).ToArray();
    }
}
