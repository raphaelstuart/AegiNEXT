using System.Text;
using System.Text.Json.Nodes;
using AegiNext.Application.Presets;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

/// <summary>验证字幕片段的稳定预设身份及旧工程的无损迁移。</summary>
public sealed class StableSubtitleStyleIdentityTests
{
    /// <summary>预设准备与套用保存同一个稳定身份，选外字幕保持原引用。</summary>
    [Fact]
    public async Task PreparationAndApplicationCarryThePresetIdInOneUndoableTransaction()
    {
        using var directory = new TemporaryProjectDirectory();
        var editor = new ProjectEditor();
        var selected = editor.AddSubtitle(new(0), new(2), "selected");
        var excluded = editor.AddSubtitle(new(2), new(4), "excluded");
        var before = editor.Snapshot;
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Dialogue", new());

        var prepared = await SubtitleStylePresetService.PrepareAsync(preset, before, directory.Path);
        var result = await SubtitleStylePresetService.ApplyAsync(preset, before, directory.Path, [selected]);
        var changes = 0;
        editor.Changed += (_, _) => changes++;
        editor.Apply("Apply style", _ => result);

        Assert.Equal(preset.Id, prepared.StylePresetId);
        Assert.Equal(preset.Id, result.Subtitles.Single(line => line.Id == selected).StylePresetId);
        Assert.Same(before.Subtitles.Single(line => line.Id == excluded), result.Subtitles.Single(line => line.Id == excluded));
        Assert.Equal(1, changes);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
        Assert.True(editor.Redo());
        Assert.Same(result, editor.Snapshot);
    }

    /// <summary>相同外观和名称不能让已有字幕的错误预设身份被当作无变化。</summary>
    [Fact]
    public void UpdatingExistingTrackStylesRepairsIdentityWithoutChangingVisuals()
    {
        var editor = new ProjectEditor();
        var presetId = Guid.NewGuid();
        editor.SetSubtitleTrackStyle(SubtitleTrack.DEFAULT_TRACK_ID, presetId, "Dialogue", new());
        var id = editor.AddSubtitle(new(0), new(2), "test");
        editor.UpdateSubtitle(id, line => line with { StylePresetId = Guid.NewGuid() });
        var before = editor.Snapshot;

        editor.SetSubtitleTrackStyle(SubtitleTrack.DEFAULT_TRACK_ID, presetId, "Dialogue", new(), updateExisting: true);

        Assert.Equal(presetId, Assert.Single(editor.Snapshot.Subtitles).StylePresetId);
        Assert.Equal(before.Subtitles[0].Style, editor.Snapshot.Subtitles[0].Style);
        Assert.Equal(before.Layers, editor.Snapshot.Layers);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
    }

    /// <summary>默认值只作用于启用自动样式的新片段；停用后保留输入的备用身份。</summary>
    [Fact]
    public void ClipCreationInheritsEnabledTrackIdentityAndOtherwisePreservesPreparedFallback()
    {
        var editor = new ProjectEditor();
        var defaultId = Guid.NewGuid();
        var fallbackId = Guid.NewGuid();
        editor.SetSubtitleTrackStyle(SubtitleTrack.DEFAULT_TRACK_ID, defaultId, "Default style", new());
        var first = new SubtitleLine { Start = new(0), End = new(2), StyleName = "Fallback", StylePresetId = fallbackId };
        editor.AddSubtitles([first], SubtitleTrack.DEFAULT_TRACK_ID);
        Assert.Equal(defaultId, editor.Snapshot.Subtitles[0].StylePresetId);

        editor.SetSubtitleTrackAutoApplyStyle(SubtitleTrack.DEFAULT_TRACK_ID, false);
        var second = first with { Id = Guid.NewGuid(), Start = new(2), End = new(4) };
        editor.AddSubtitles([second], SubtitleTrack.DEFAULT_TRACK_ID);

        Assert.Equal(fallbackId, editor.Snapshot.Subtitles[1].StylePresetId);
        Assert.Equal("Fallback", editor.Snapshot.Subtitles[1].StyleName);
    }

    /// <summary>剪贴板和拆合延续片段样式身份；合并仍使用首句基础样式。</summary>
    [Fact]
    public void ClipboardSplitAndMergePreserveTheEstablishedStyleIdentity()
    {
        var editor = new ProjectEditor();
        var presetId = Guid.NewGuid();
        var id = editor.AddSubtitle(new(0), new(4), "abcd");
        editor.UpdateSubtitle(id, line => line with { StylePresetId = presetId });
        var content = ProjectEditingOperations.CaptureClips(editor.Snapshot, [id], id);
        var pasted = ProjectEditingOperations.PasteClips(editor.Snapshot, content, new(10));
        Assert.Equal(presetId, pasted.Document.Subtitles.Single(line => line.Id != id).StylePresetId);

        var split = ProjectEditingOperations.SplitSubtitle(editor.Snapshot, id, new(2), 2);
        Assert.All(split.Subtitles, line => Assert.Equal(presetId, line.StylePresetId));
        var second = split.Subtitles[1];
        var changed = split with { Subtitles = split.Subtitles.SetItem(1, second with { StylePresetId = Guid.NewGuid() }) };
        var merged = ProjectEditingOperations.MergeSubtitles(changed, id, second.Id);
        Assert.Equal(presetId, Assert.Single(merged.Subtitles).StylePresetId);
    }

    /// <summary>旧工程不会根据当前轨道默认样式猜测历史片段身份，也不会改变精确时钟。</summary>
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void LegacyProjectsGainNoGuessedIdentityAndKeepRationalTimes(int version)
    {
        var editor = new ProjectEditor();
        editor.SetSubtitleTrackStyle(SubtitleTrack.DEFAULT_TRACK_ID, Guid.NewGuid(), "Dialogue", new());
        editor.AddSubtitle(new(1001, 30000), new(2002, 30000), "legacy");
        var original = editor.Snapshot;
        var json = JsonNode.Parse(ProjectStore.Serialize(original))!.AsObject();
        json["version"] = version;
        json["subtitles"]![0]!.AsObject().Remove("stylePresetId");

        var restored = ProjectStore.Deserialize(Encoding.UTF8.GetBytes(json.ToJsonString()));

        Assert.Equal(7, restored.Version);
        Assert.Null(Assert.Single(restored.Subtitles).StylePresetId);
        Assert.Equal(original.Subtitles[0].Start, restored.Subtitles[0].Start);
        Assert.Equal(original.Subtitles[0].End, restored.Subtitles[0].End);
        var expected = original with
        {
            Subtitles = original.Subtitles.SetItem(0, original.Subtitles[0] with { StylePresetId = null })
        };
        Assert.Equal(ProjectStore.Serialize(expected), ProjectStore.Serialize(restored));
        Assert.Equal(version, json["version"]!.GetValue<int>());
    }

    /// <summary>当前工程能保存不存在于本机全局库的身份，并允许可选身份字段缺失。</summary>
    [Fact]
    public void VersionSevenRoundTripsStableIdentityAndAllowsAnUnboundClip()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(2), "test");
        var presetId = Guid.NewGuid();
        editor.UpdateSubtitle(id, line => line with { StylePresetId = presetId });
        var bytes = ProjectStore.Serialize(editor.Snapshot);
        var restored = ProjectStore.Deserialize(bytes);
        Assert.Equal(7, restored.Version);
        Assert.Equal(presetId, Assert.Single(restored.Subtitles).StylePresetId);
        Assert.Equal(bytes, ProjectStore.Serialize(restored));

        var json = JsonNode.Parse(bytes)!;
        json["subtitles"]![0]!.AsObject().Remove("stylePresetId");
        Assert.Null(ProjectStore.Deserialize(Encoding.UTF8.GetBytes(json.ToJsonString())).Subtitles[0].StylePresetId);
    }

    /// <summary>无效、空和未知身份字段不得悄悄退化为未绑定。</summary>
    [Theory]
    [InlineData("\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("\"invalid\"")]
    [InlineData("0")]
    [InlineData("{}")]
    public void MalformedStableIdentityIsRejected(string value)
    {
        var editor = new ProjectEditor();
        editor.AddSubtitle(new(0), new(2), "test");
        var json = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!;
        json["subtitles"]![0]!["stylePresetId"] = JsonNode.Parse(value);
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(json.ToJsonString())));
    }

    /// <summary>新增可选身份后仍拒绝未知字段。</summary>
    [Fact]
    public void UnknownSubtitleIdentityFieldsRemainStrictlyRejected()
    {
        var editor = new ProjectEditor();
        editor.AddSubtitle(new(0), new(2), "test");
        var json = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!;
        json["subtitles"]![0]!["unknownStylePresetId"] = Guid.NewGuid().ToString();
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(json.ToJsonString())));
    }
}
