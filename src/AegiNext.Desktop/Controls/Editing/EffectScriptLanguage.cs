using System.Text.RegularExpressions;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Controls;

internal static class EffectScriptLanguage
{
    private static readonly Regex tokens = new("#[^\\r\\n]*|\"[^\"\\r\\n]*\"?|mask-node\\(\\s*\\d+\\s*,\\s*\\d+\\s*\\)\\.(?:position|in-handle|out-handle)|[+-]?(?:\\d+(?:\\.\\d+)?|\\.\\d+)(?:ms|s)?|[a-z][a-z-]*|\\s+|.",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex keyPrefix = new(@"^\s*at\s+\S+\s+", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex selectorWhitespace = new(@"mask-node\(\s*(\d+)\s*,\s*(\d+)\s*\)", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly HashSet<string> keywords = ["effect", "version", "short-clip", "compress", "reject", "segment", "fixed", "flex", "at", "end"];
    private static readonly string[] interpolation = ["linear", "hold", "ease-in", "ease-out", "ease-in-out", "power(2)"];

    internal static IReadOnlyList<SyntaxToken> Tokenize(string source)
    {
        var result = new List<SyntaxToken>();
        foreach (Match match in tokens.Matches(source))
        {
            var value = match.Value;
            var kind = value[0] switch
            {
                '#' => SyntaxTokenKind.COMMENT,
                '"' => SyntaxTokenKind.STRING,
                _ when keywords.Contains(value) => SyntaxTokenKind.KEYWORD,
                _ when EffectScriptPropertyMetadata.TryGetProperty(value, out _) || value == "mask-node" => SyntaxTokenKind.PROPERTY,
                _ when value is "base" or "offset" or "factor" or "rgba" => SyntaxTokenKind.FUNCTION,
                _ when interpolation.Contains(value, StringComparer.Ordinal) || value == "power" => SyntaxTokenKind.INTERPOLATION,
                _ when char.IsDigit(value[0]) || value[0] is '+' or '-' or '.' && value.Length > 1 => SyntaxTokenKind.NUMBER,
                _ => SyntaxTokenKind.TEXT
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

        var keyStart = keyPrefix.Match(prefix);
        if (keyStart.Success)
        {
            var propertyStart = keyStart.Length;
            var depth = 0;
            var end = propertyStart;
            for (; end < prefix.Length; end++)
            {
                var character = prefix[end];
                if (character == '(')
                {
                    depth++;
                }
                else if (character == ')')
                {
                    depth--;
                }
                else if (char.IsWhiteSpace(character) && depth == 0)
                {
                    break;
                }
            }

            if (end == prefix.Length)
            {
                var partialProperty = prefix[propertyStart..];
                var propertyCandidates = EffectScriptPropertyMetadata.PropertyNames.AsEnumerable();
                var closing = partialProperty.IndexOf(')', StringComparison.Ordinal);
                if (partialProperty.StartsWith("mask-node(", StringComparison.Ordinal) && closing >= 0)
                {
                    var stem = partialProperty[..(closing + 1)] + ".";
                    propertyCandidates = [stem + "position", stem + "in-handle", stem + "out-handle"];
                }

                return propertyCandidates.Where(name => name.StartsWith(partialProperty, StringComparison.Ordinal))
                    .Select(name => new EffectScriptCompletion(lineStart + propertyStart, partialProperty.Length, name, name,
                        ValueHint(name))).ToArray();
            }
        }

        var replacementStart = caret;
        while (replacementStart > lineStart && (char.IsLetter(source[replacementStart - 1]) || source[replacementStart - 1] == '-'))
        {
            replacementStart--;
        }

        var partial = source[replacementStart..caret];
        var before = source[lineStart..replacementStart].Trim();
        var words = selectorWhitespace.Replace(before, "mask-node($1,$2)")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var candidates = new List<(string Text, string Hint)>();
        if (words.Length == 0)
        {
            if (string.IsNullOrWhiteSpace(source[..lineStart]))
            {
                candidates.Add(("effect \"my-effect\" version 1", Localization.Get("Settings.ScriptHintHeader")));
            }
            else
            {
                var prior = source[..lineStart].Split('\n').Select(line => line.Split('#', 2)[0].Trim()).Where(line => line.Length > 0).ToArray();
                var inside = prior.Count(line => line.StartsWith("segment ", StringComparison.Ordinal)) > prior.Count(line => line == "end");
                if (inside)
                {
                    candidates.Add(("at 0 ", Localization.Get("Settings.ScriptHintAt")));
                    candidates.Add(("at 1 ", Localization.Get("Settings.ScriptHintEndpoints")));
                    candidates.Add(("end", Localization.Get("Settings.ScriptHintEnd")));
                }
                else if (!prior.Any(line => line.StartsWith("short-clip ", StringComparison.Ordinal)))
                {
                    candidates.Add(("short-clip compress", Localization.Get("Settings.ScriptHintCompress")));
                    candidates.Add(("short-clip reject", Localization.Get("Settings.ScriptHintReject")));
                }
                else
                {
                    candidates.Add(("segment enter fixed 300ms", Localization.Get("Settings.ScriptHintFixed")));
                    candidates.Add(("segment stay flex 1", Localization.Get("Settings.ScriptHintFlex")));
                }
            }
        }
        else if (words[0] == "short-clip")
        {
            candidates.Add(("compress", Localization.Get("Settings.ScriptHintCompress")));
            candidates.Add(("reject", Localization.Get("Settings.ScriptHintReject")));
        }
        else if (words[0] == "segment" && words.Length == 2)
        {
            candidates.Add(("fixed 300ms", Localization.Get("Settings.ScriptHintFixed")));
            candidates.Add(("flex 1", Localization.Get("Settings.ScriptHintFlex")));
        }
        else if (words[0] == "at" && words.Length == 2)
        {
            candidates.AddRange(EffectScriptPropertyMetadata.PropertyNames.Select(value => (value, ValueHint(value))));
        }
        else if (words[0] == "at" && words.Length == 3)
        {
            var vector = EffectScriptPropertyMetadata.TryGetProperty(words[2], out var property) &&
                AnimationPropertyMetadata.GetValueKind(EffectScriptPropertyMetadata.GetAnimationProperty(property)) == AnimationValueKind.VECTOR;
            if (EffectScriptPropertyMetadata.TryGetProperty(words[2], out var colorProperty) &&
                AnimationPropertyMetadata.GetValueKind(EffectScriptPropertyMetadata.GetAnimationProperty(colorProperty)) == AnimationValueKind.COLOR)
            {
                candidates.Add(("base", Localization.Get("Settings.ScriptHintBase")));
                candidates.Add(("rgba(1, 1, 1, 1)", Localization.Get("Settings.ScriptHintColor")));
            }
            else if (words[2] == "path-progress")
            {
                candidates.Add(("0", Localization.Get("Settings.ScriptHintPath")));
                candidates.Add(("1", Localization.Get("Settings.ScriptHintPath")));
            }
            else
            {
                candidates.Add(("base", Localization.Get("Settings.ScriptHintBase")));
                candidates.Add((vector ? "offset(0, 0)" : "offset(0)", Localization.Get("Settings.ScriptHintOffset")));
                candidates.Add((vector ? "factor(1, 1)" : "factor(1)", Localization.Get("Settings.ScriptHintFactor")));
                candidates.Add((vector ? "(0, 0)" : "0", Localization.Get("Settings.ScriptHintAbsolute")));
            }
        }
        else if (words[0] == "at" && words.Length >= 4 && (!before.Contains('(', StringComparison.Ordinal) || before.Contains(')', StringComparison.Ordinal)))
        {
            candidates.AddRange(interpolation.Select(value => (value, Localization.Get("Settings.ScriptHintInterpolation"))));
        }

        return candidates.Where(item => item.Text.StartsWith(partial, StringComparison.Ordinal))
            .Select(item => new EffectScriptCompletion(replacementStart, caret - replacementStart, item.Text, item.Text, item.Hint)).ToArray();
    }

    private static string ValueHint(string propertyName)
    {
        var kind = EffectScriptPropertyMetadata.TryGetProperty(propertyName, out var property)
            ? AnimationPropertyMetadata.GetValueKind(EffectScriptPropertyMetadata.GetAnimationProperty(property)) : AnimationValueKind.SCALAR;
        return Localization.Get("Settings." + (kind switch
        {
            AnimationValueKind.VECTOR => "ScriptHintVector",
            AnimationValueKind.COLOR => "ScriptHintColor",
            _ => "ScriptHintScalar"
        }));
    }
}
