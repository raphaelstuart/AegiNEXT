using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Settings;

/// <summary>个人时间线配色，HEX 使用 sRGB 和 RGBA 后缀顺序。</summary>
public sealed record TimelineClipPalette
{
    public bool AdaptToTheme { get; init; } = true;
    public string SelectedClip { get; init; } = "#4568DCB8";
    public string InactiveClip { get; init; } = "#8490A033";
    public string StartLine { get; init; } = "#75D4A4";
    public string EndLine { get; init; } = "#EB8795";
    public string SelectedRangeFill { get; init; } = "#5273E824";
    public string InactiveRangeFill { get; init; } = "#8792A010";
    public string MediaRangeFill { get; init; } = "#FFFFFF08";

    /// <summary>验证允许透明度的 HEX 片段、边界线和区间填充颜色。</summary>
    public void Validate()
    {
        foreach (var text in new[] { SelectedClip, InactiveClip, StartLine, EndLine, SelectedRangeFill, InactiveRangeFill, MediaRangeFill })
        {
            if (text is null || !ColorHexCodec.TryParse(text, 1, true, out _))
            {
                throw new InvalidDataException("时间轴 clip 颜色必须是有效的 HEX 颜色。");
            }
        }
    }
}
