using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Effects;

internal sealed class EffectScriptV2Parser
{
    private static readonly Regex headerPattern = Pattern(@"^effect\s+""(?<id>[a-z][a-z0-9.-]{0,63})""\s+version\s+2$");
    private static readonly Regex scopePattern = Pattern(@"^scope\s+(?<name>[a-z][a-z0-9.-]{0,63})\s+(?<target>current|subtitle|range\(\s*\d+\s*,\s*\d+\s*\))$");
    private static readonly Regex rangePattern = Pattern(@"^range\(\s*(?<start>\d+)\s*,\s*(?<count>\d+)\s*\)$");
    private static readonly Regex segmentPattern = Pattern(@"^segment\s+(?<name>[a-z][a-z0-9.-]{0,63})\s+(?<kind>fixed|flex)\s+(?<amount>\S+)(?:\s+(?<modifiers>.*))?$");
    private static readonly Regex fieldPattern = Pattern(@"^(?<field>[a-z][a-z-]*)\s+(?<value>.+)$");
    private static readonly Regex chunkPattern = Pattern(@"^chunk\(\s*(?<count>\d+)\s*\)$");
    private readonly ImmutableArray<EffectScriptScope>.Builder scopes = ImmutableArray.CreateBuilder<EffectScriptScope>();
    private readonly ImmutableArray<EffectScriptSegment>.Builder segments = ImmutableArray.CreateBuilder<EffectScriptSegment>();
    private readonly ImmutableArray<EffectScriptKeyframe>.Builder keys = ImmutableArray.CreateBuilder<EffectScriptKeyframe>();
    private readonly HashSet<string> scopeNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> fields = new(StringComparer.Ordinal);
    private string? id;
    private EffectScriptShortClipPolicy? policy;
    private string? scopeName;
    private EffectScriptTargetSelector? target;
    private EffectScriptUnit unit = new(EffectScriptUnitKind.GROUP);
    private MediaTime delay = MediaTime.Zero;
    private MediaTime stagger = MediaTime.Zero;
    private EffectScriptOrder order;
    private SubtitleAnimationState state;
    private int scopeLine;
    private int scopeColumn;
    private bool fieldsClosed;
    private string? segmentName;
    private MediaTime? fixedDuration;
    private decimal flexWeight;
    private int repeatCount = 1;
    private bool pingPong;
    private MediaTime? cycleDuration;
    private int segmentLine;
    private int segmentColumn;
    private int segmentCount;
    private int keyCount;

    internal static EffectScript Parse(string source)
    {
        return new EffectScriptV2Parser().ParseCore(source);
    }

    private EffectScript ParseCore(string source)
    {
        var lines = source.TrimStart('\uFEFF').Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = index + 1;
            var text = EffectScriptV2Lexer.StripComment(lines[index], line).Trim();
            var column = lines[index].TakeWhile(char.IsWhiteSpace).Count() + 1;
            if (text.Length == 0)
            {
                continue;
            }
            if (id is null)
            {
                var header = headerPattern.Match(text);
                if (!header.Success)
                {
                    throw new EffectScriptException("首条指令应为 effect \"稳定标识\" version 2。", line, column);
                }
                id = header.Groups["id"].Value;
            }
            else if (policy is null)
            {
                policy = text switch
                {
                    "short-clip compress" => EffectScriptShortClipPolicy.COMPRESS,
                    "short-clip reject" => EffectScriptShortClipPolicy.REJECT,
                    _ => throw new EffectScriptException("应显式指定 short-clip compress 或 short-clip reject。", line, column)
                };
            }
            else if (scopeName is null)
            {
                StartScope(text, line, column);
            }
            else if (segmentName is not null)
            {
                if (text == "end")
                {
                    EndSegment();
                }
                else
                {
                    if (++keyCount > EffectScriptValidator.MAXIMUM_KEYFRAMES)
                    {
                        throw new EffectScriptException("关键帧数量超过脚本聚合预算。", line, column);
                    }
                    keys.Add(EffectScriptParser.ParseKeyframe(text, line, column));
                }
            }
            else if (text == "end")
            {
                scopes.Add(new(scopeName, target!, segments.ToImmutable(), scopeLine, scopeColumn)
                {
                    Unit = unit,
                    Delay = delay,
                    Stagger = stagger,
                    Order = order,
                    State = state
                });
                segments.Clear();
                scopeName = null;
            }
            else if (text.StartsWith("segment", StringComparison.Ordinal) &&
                (text.Length == 7 || char.IsWhiteSpace(text[7])))
            {
                StartSegment(text, line, column);
            }
            else
            {
                ParseField(text, line, column);
            }
        }
        if (id is null || policy is null)
        {
            throw new EffectScriptException("缺少脚本标识或短片段规则。");
        }
        if (segmentName is not null)
        {
            throw new EffectScriptException("时间段缺少 end。", segmentLine, segmentColumn);
        }
        if (scopeName is not null)
        {
            throw new EffectScriptException("作用范围缺少 end。", scopeLine, scopeColumn);
        }
        var result = new EffectScript(id, policy.Value, []) { Version = 2, Scopes = scopes.ToImmutable() };
        EffectScriptValidator.Validate(result);
        return result;
    }

    private void StartScope(string text, int line, int column)
    {
        var scope = scopePattern.Match(text);
        if (!scope.Success)
        {
            throw new EffectScriptException("应开始 scope 名称 current、subtitle 或 range(一基起点, 数量)。", line, column);
        }
        if (scopes.Count >= EffectScriptValidator.MAXIMUM_SCOPES)
        {
            throw new EffectScriptException("作用范围数量超过预算。", line, column);
        }
        scopeName = scope.Groups["name"].Value;
        if (!scopeNames.Add(scopeName))
        {
            throw new EffectScriptException("作用范围名称不能重复。", line, column + scope.Groups["name"].Index);
        }
        target = ParseTarget(scope.Groups["target"].Value, line, column + scope.Groups["target"].Index);
        unit = new(EffectScriptUnitKind.GROUP);
        delay = MediaTime.Zero;
        stagger = MediaTime.Zero;
        order = EffectScriptOrder.FORWARD;
        state = SubtitleAnimationState.NORMAL;
        fields.Clear();
        fieldsClosed = false;
        scopeLine = line;
        scopeColumn = column;
    }

    private void StartSegment(string text, int line, int column)
    {
        var segment = segmentPattern.Match(text);
        if (!segment.Success)
        {
            throw new EffectScriptException("应开始 segment 名称 fixed 时长 或 segment 名称 flex 权重。", line, column);
        }
        if (++segmentCount > EffectScriptValidator.MAXIMUM_SEGMENTS)
        {
            throw new EffectScriptException("时间段数量超过脚本聚合预算。", line, column);
        }
        fieldsClosed = true;
        segmentName = segment.Groups["name"].Value;
        var amount = segment.Groups["amount"].Value;
        var amountColumn = column + segment.Groups["amount"].Index;
        fixedDuration = segment.Groups["kind"].Value == "fixed" ? EffectScriptParser.ParseDuration(amount, line, amountColumn) : null;
        flexWeight = fixedDuration.HasValue ? 0 : EffectScriptParser.ParseDecimal(amount, line, amountColumn);
        repeatCount = 1;
        pingPong = false;
        cycleDuration = null;
        var modifiers = segment.Groups["modifiers"];
        ParseModifiers(modifiers.Value, line, column + modifiers.Index);
        if (!fixedDuration.HasValue && pingPong && !cycleDuration.HasValue)
        {
            throw new EffectScriptException("自由段的 pingpong 必须同时指定 cycle 时长。", line, column);
        }
        segmentLine = line;
        segmentColumn = column;
    }

    private void ParseModifiers(string text, int line, int column)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var offset = 0;
        while (offset < text.Length)
        {
            EffectScriptV2Lexer.SkipWhitespace(text, ref offset);
            if (offset == text.Length)
            {
                break;
            }
            var start = offset;
            var token = ReadWord(text, ref offset);
            if (!seen.Add(token))
            {
                throw new EffectScriptException("时间段修饰不能重复。", line, column + start);
            }
            switch (token)
            {
                case "pingpong":
                    pingPong = true;
                    break;
                case "repeat":
                    if (!fixedDuration.HasValue)
                    {
                        throw new EffectScriptException("repeat 仅用于固定段。", line, column + start);
                    }
                    EffectScriptV2Lexer.SkipWhitespace(text, ref offset);
                    var repeatStart = offset;
                    repeatCount = ParsePositiveInteger(ReadWord(text, ref offset), line, column + repeatStart);
                    break;
                case "cycle":
                    if (fixedDuration.HasValue)
                    {
                        throw new EffectScriptException("cycle 仅用于自由段。", line, column + start);
                    }
                    EffectScriptV2Lexer.SkipWhitespace(text, ref offset);
                    var durationStart = offset;
                    cycleDuration = EffectScriptParser.ParseDuration(ReadWord(text, ref offset), line, column + durationStart);
                    break;
                default:
                    throw new EffectScriptException($"未知时间段修饰：{token}。", line, column + start);
            }
        }
    }

    private void EndSegment()
    {
        segments.Add(new(segmentName!, fixedDuration, flexWeight, keys.ToImmutable(), segmentLine, segmentColumn)
        {
            RepeatCount = repeatCount,
            PingPong = pingPong,
            CycleDuration = cycleDuration
        });
        keys.Clear();
        segmentName = null;
    }

    private void ParseField(string text, int line, int column)
    {
        if (fieldsClosed)
        {
            throw new EffectScriptException("作用范围字段只能声明在首个时间段之前。", line, column);
        }
        var match = fieldPattern.Match(text);
        if (!match.Success)
        {
            throw new EffectScriptException("应声明作用范围字段或开始时间段。", line, column);
        }
        var name = match.Groups["field"].Value;
        if (!fields.Add(name))
        {
            throw new EffectScriptException("作用范围字段不能重复。", line, column);
        }
        var value = match.Groups["value"].Value;
        var valueColumn = column + match.Groups["value"].Index;
        switch (name)
        {
            case "unit":
                unit = ParseUnit(value, line, valueColumn);
                break;
            case "delay":
                delay = EffectScriptParser.ParseDuration(value, line, valueColumn, true);
                break;
            case "stagger":
                stagger = EffectScriptParser.ParseDuration(value, line, valueColumn, true);
                break;
            case "order":
                order = value switch
                {
                    "forward" => EffectScriptOrder.FORWARD,
                    "reverse" => EffectScriptOrder.REVERSE,
                    _ => throw new EffectScriptException("order 应为 forward 或 reverse。", line, valueColumn)
                };
                break;
            case "state":
                state = value switch
                {
                    "normal" => SubtitleAnimationState.NORMAL,
                    "active" => SubtitleAnimationState.ACTIVE,
                    "inactive" => SubtitleAnimationState.INACTIVE,
                    _ => throw new EffectScriptException("state 应为 normal、active 或 inactive。", line, valueColumn)
                };
                break;
            default:
                throw new EffectScriptException($"未知作用范围字段：{name}。", line, column);
        }
    }

    private static EffectScriptTargetSelector ParseTarget(string text, int line, int column)
    {
        if (text == "current")
        {
            return new(EffectScriptTargetKind.CURRENT);
        }
        if (text == "subtitle")
        {
            return new(EffectScriptTargetKind.SUBTITLE);
        }
        var match = rangePattern.Match(text);
        var start = ParsePositiveInteger(match.Groups["start"].Value, line, column + match.Groups["start"].Index);
        var count = ParsePositiveInteger(match.Groups["count"].Value, line, column + match.Groups["count"].Index);
        if ((long)start + count - 1 > int.MaxValue)
        {
            throw new EffectScriptException("固定字素范围超出整数表示范围。", line, column);
        }
        return new(EffectScriptTargetKind.RANGE, start, count);
    }

    private static EffectScriptUnit ParseUnit(string text, int line, int column)
    {
        var kind = text switch
        {
            "group" => EffectScriptUnitKind.GROUP,
            "grapheme" => EffectScriptUnitKind.GRAPHEME,
            "word" => EffectScriptUnitKind.WORD,
            "line" => EffectScriptUnitKind.LINE,
            "paragraph" => EffectScriptUnitKind.PARAGRAPH,
            _ => (EffectScriptUnitKind?)null
        };
        if (kind.HasValue)
        {
            return new(kind.Value);
        }
        var chunk = chunkPattern.Match(text);
        if (chunk.Success)
        {
            return new(EffectScriptUnitKind.CHUNK)
            {
                Count = ParsePositiveInteger(chunk.Groups["count"].Value, line, column + chunk.Groups["count"].Index)
            };
        }
        if (text.StartsWith("split(", StringComparison.Ordinal) && text.EndsWith(')'))
        {
            return ParseSplit(text[6..^1], line, column + 6);
        }
        throw new EffectScriptException("unit 应为 group、grapheme、chunk(数量)、word、line、paragraph 或 split(字符串...)。", line, column);
    }

    private static EffectScriptUnit ParseSplit(string text, int line, int column)
    {
        var delimiters = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var offset = 0;
        while (true)
        {
            EffectScriptV2Lexer.SkipWhitespace(text, ref offset);
            var start = offset;
            var delimiter = EffectScriptV2Lexer.ReadString(text, ref offset, line, column);
            if (!seen.Add(delimiter))
            {
                throw new EffectScriptException("split 分隔符不能重复。", line, column + start);
            }
            delimiters.Add(delimiter);
            EffectScriptV2Lexer.SkipWhitespace(text, ref offset);
            if (offset == text.Length)
            {
                break;
            }
            if (text[offset] != ',')
            {
                throw new EffectScriptException("split 分隔符之间必须使用逗号。", line, column + offset);
            }
            offset++;
        }
        return new(EffectScriptUnitKind.SPLIT) { Delimiters = delimiters.ToImmutable() };
    }

    private static int ParsePositiveInteger(string text, int line, int column)
    {
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var result) || result <= 0)
        {
            throw new EffectScriptException("应使用可表示的正整数。", line, column);
        }
        return result;
    }

    private static string ReadWord(string text, ref int offset)
    {
        var start = offset;
        while (offset < text.Length && !char.IsWhiteSpace(text[offset]))
        {
            offset++;
        }
        return text[start..offset];
    }

    private static Regex Pattern(string pattern)
    {
        return new(pattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    }
}
