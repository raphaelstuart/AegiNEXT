using System.Collections.Immutable;

namespace AegiNext.Core.Presets;

/// <summary>原始字体字节、规范文件名及小写 SHA-256；不引用来源工程目录。</summary>
public sealed record EmbeddedSubtitleFont(string FileName, string Sha256, ImmutableArray<byte> Data);
