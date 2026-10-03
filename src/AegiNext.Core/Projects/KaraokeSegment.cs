using AegiNext.Core.Timing;

namespace AegiNext.Core.Projects;

/// <summary>以 UTF-16 区间选择完整文本元素；Start/End 相对字幕层内容原点，完成后保持高亮。</summary>
public sealed record KaraokeSegment(int Utf16Start, int Utf16Length, MediaTime Start, MediaTime End, SceneColor HighlightColor);
