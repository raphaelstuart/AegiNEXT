namespace AegiNext.Core.Projects;

/// <summary>完整字素组成的 UTF-16 半开范围及其局部样式覆盖。</summary>
public sealed record SubtitleInlineSpan(int Utf16Start, int Utf16Length, SubtitleInlineStyleOverride Style);
