using System.Text.Json;
using AegiNext.Core.Projects;
using AegiNext.Media.Encoding;

namespace AegiNext.Media.Tests.Encoding;

public sealed class ExportWireContentTests
{
    [Fact]
    public void RealWorkerRequestCarriesInlineFontsClipIdentityAndExactContentTime()
    {
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "font.ttf");
        var line = new SubtitleLine
        {
            Text = "a😀", InlineSpans = [new(1, 2, new() { FontAssetId = font.Id, Underline = true, Fill = new(4, -0.2, 1, 0.5) })],
            Karaoke = [new(1, 2, new(1, 3), new(2, 3), SceneColor.White)
            {
                HighlightKind = KaraokeHighlightKind.OUTLINE_STEP,
                InactiveStyle = new() { StrokeWidth = 0 }, ActiveStyle = new() { Fill = new(1, 0, 0) }
            }]
        };
        var document = new ProjectDocument
        {
            Assets = [font], Subtitles = [line],
            Layers = [new() { Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
        };
        ProjectValidator.Validate(document);
        var job = new ExportWorkerJob(JsonSerializer.SerializeToElement(document, ExportWire.Options), Path.GetTempPath(),
            Path.GetTempPath(), ".mkv", VideoCodec.H264, "medium", 18, AudioExportMode.None, 192, "ffmpeg");
        var transmitted = JsonSerializer.Deserialize<ExportWorkerJob>(JsonSerializer.Serialize(job, ExportWire.Options), ExportWire.Options)!;
        var restored = transmitted.Project.GetProperty("subtitles")[0];
        var clip = restored.GetProperty("karaoke")[0];
        Assert.Equal(ProjectDocument.CURRENT_VERSION, transmitted.Project.GetProperty("version").GetInt32());
        Assert.Equal(line.InlineSpans[0], restored.GetProperty("inlineSpans")[0].Deserialize<SubtitleInlineSpan>(ExportWire.Options));
        Assert.Equal(line.Karaoke[0].Id, clip.GetProperty("id").GetGuid());
        Assert.Equal("OUTLINE_STEP", clip.GetProperty("highlightKind").GetString());
        Assert.Equal(1, clip.GetProperty("start").GetProperty("numerator").GetInt64());
        Assert.Equal(3, clip.GetProperty("start").GetProperty("denominator").GetInt64());
        Assert.Equal(2, clip.GetProperty("end").GetProperty("numerator").GetInt64());
        Assert.Equal(3, clip.GetProperty("end").GetProperty("denominator").GetInt64());
        Assert.Equal(line.Karaoke[0].ActiveStyle, clip.GetProperty("activeStyle").Deserialize<KaraokeVisualStyleOverride>(ExportWire.Options));
        Assert.Equal(font, transmitted.Project.GetProperty("assets")[0].Deserialize<ProjectAsset>(ExportWire.Options));
    }
}
