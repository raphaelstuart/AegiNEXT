using AegiNext.Core.Projects;

namespace AegiNext.Core.Tests.Projects;

/// <summary>验证字幕样式身份的模型边界，不要求本机全局库存在对应预设。</summary>
public sealed class SubtitleStyleIdentityTests
{
    /// <summary>未关联和外部预设关联均可成为有效工程快照。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullableOrNonemptyPresetIdentityIsValidWithoutAnExternalLibrary(bool associated)
    {
        var line = new SubtitleLine { StylePresetId = associated ? Guid.NewGuid() : null };
        ProjectValidator.Validate(Document(line));
    }

    /// <summary>空 Guid 不是合法样式关联。</summary>
    [Fact]
    public void EmptyPresetIdentityIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(new() { StylePresetId = Guid.Empty })));
    }

    private static ProjectDocument Document(SubtitleLine line)
    {
        return new()
        {
            Subtitles = [line],
            Layers = [new() { Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
        };
    }
}
