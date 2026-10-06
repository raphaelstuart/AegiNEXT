using System.Text.Json;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Colors;

namespace AegiNext.Desktop.Tests;

public sealed class TimelineClipPaletteTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void EveryClipColorAcceptsRgbaAndRejectsInvalidOrMissingHex(int field)
    {
        var palette = WithColor(new(), field, "#11223380");
        palette.Validate();
        Assert.True(ColorHexCodec.TryParse(Color(palette, field), 1, true, out var value));
        Assert.Equal(128 / 255d, value.Alpha);
        Assert.Equal("#11223380", ColorHexCodec.Format(value, true));

        foreach (var invalid in new[] { "#bad", "#GGFFFF", "red", string.Empty, null })
        {
            Assert.Throws<InvalidDataException>(() => WithColor(new(), field, invalid!).Validate());
        }
    }

    [Fact]
    public void DefaultColorsAdaptToBothThemesAndCustomColorsRemainExact()
    {
        var original = new TimelineClipPalette();
        Assert.Same(original, TimelineClipPalettes.Resolve(original, false));
        var light = TimelineClipPalettes.Resolve(original, true);
        light.Validate();
        Assert.NotEqual(original, light);
        Assert.Equal(new(), original);
        var custom = original with { AdaptToTheme = false };
        Assert.Same(custom, TimelineClipPalettes.Resolve(custom, true));
        Assert.Same(custom, TimelineClipPalettes.Resolve(custom, false));
        var changed = original with { SelectedClip = "#11223380" };
        Assert.Same(changed, TimelineClipPalettes.Resolve(changed, true));
    }

    [Fact]
    public async Task PersonalClipColorsRoundTripAndOldPreferencesReceiveThemeAwareDefaults()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var store = new WorkbenchPreferencesStore(directory.Path);
        var path = Path.Combine(directory.Path, "preferences.json");
        var previousJson = """{"Version":1,"Language":"zh-CN","Theme":2,"Volume":0.25}""";
        await File.WriteAllTextAsync(path, previousJson);

        var previous = store.Load();
        Assert.Null(store.LoadError);
        Assert.Equal(new(), previous.TimelineClips);
        Assert.Equal(previousJson, await File.ReadAllTextAsync(path));
        var palette = new TimelineClipPalette
        {
            AdaptToTheme = false,
            SelectedClip = "#11223380", InactiveClip = "#22334440",
            StartLine = "#33445560", EndLine = "#44556670",
            SelectedRangeFill = "#55667720", InactiveRangeFill = "#66778810"
        };
        var expected = previous with { TimelineClips = palette };
        await store.SaveAsync(expected);
        var actual = store.Load();

        Assert.Equal(expected, actual);
        Assert.Equal(expected.GetHashCode(), actual.GetHashCode());
        Assert.Equal(expected, JsonSerializer.Deserialize<WorkbenchPreferences>(JsonSerializer.Serialize(expected)));
        Assert.Equal("zh-CN", actual.Language);
        Assert.Equal(0.25f, actual.Volume);
        Assert.Single(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public void EveryClipColorAndThemeFlagParticipatesInPreferenceEquality()
    {
        var original = new WorkbenchPreferences();
        var changes = Enumerable.Range(0, 6)
            .Select(field => WithColor(original.TimelineClips, field, "#11223380"))
            .Append(original.TimelineClips with { AdaptToTheme = false });
        foreach (var palette in changes)
        {
            var changed = original with { TimelineClips = palette };
            Assert.NotEqual(original, changed);
            var copy = changed with { TimelineClips = palette with { } };
            Assert.Equal(changed, copy);
            Assert.Equal(changed.GetHashCode(), copy.GetHashCode());
        }

        Assert.Throws<InvalidDataException>(() => (original with { TimelineClips = null! }).Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void EachDraftCommitsOnlyItsColorOnceAndPreservesAnotherInvalidDraftDuringRefresh(int field)
    {
        var original = new WorkbenchPreferences { AccentColor = "#AABBCC", AudioGraph = AudioGraphPalettes.Get(2) };
        var model = new ColorsSettingsViewModel(original);
        var drafts = Drafts(model);
        var invalidDraft = drafts[(field + 1) % drafts.Length];
        invalidDraft.HexText = "#bad";
        Assert.False(invalidDraft.TryCommit());
        var changes = new List<SettingsColorsChangedEventArgs>();
        model.Changed += (_, args) => changes.Add(args);
        drafts[field].HexText = "#11223380";
        Assert.True(drafts[field].TryCommit());

        var change = Assert.Single(changes);
        var expected = WithColor(original.TimelineClips, field, "#11223380") with { AdaptToTheme = false };
        Assert.Equal(expected, change.TimelineClips);
        Assert.Equal(original.AccentColor, change.AccentColor);
        Assert.Equal(original.AudioGraph, change.AudioGraph);
        Assert.Equal(128 / 255d, drafts[field].Value.Alpha);
        Assert.False(drafts[field].IsDirty);
        Assert.True(drafts[field].TryCommit());
        Assert.Single(changes);

        model.UpdatePreferences(original with { TimelineClips = expected, Theme = WorkbenchTheme.LIGHT });
        model.RefreshLanguage();
        Assert.Equal("#bad", invalidDraft.HexText);
        Assert.True(invalidDraft.HasError);
        Assert.Single(changes);
        invalidDraft.Restore("HexText");
        Assert.False(invalidDraft.HasError);
        Assert.False(invalidDraft.IsDirty);
        Assert.Equal("#11223380", drafts[field].HexText);
    }

    [Fact]
    public void RestoringDefaultsClearsEveryInvalidClipDraftAndCommitsAllColorsOnce()
    {
        var original = new WorkbenchPreferences
        {
            AccentColor = "#AABBCC", AudioGraph = AudioGraphPalettes.Get(2),
            TimelineClips = new() { AdaptToTheme = false, SelectedClip = "#11223380" }
        };
        var model = new ColorsSettingsViewModel(original);
        foreach (var draft in Drafts(model).Append(model.AccentDraft).Append(model.HighDraft))
        {
            draft.HexText = "#bad";
            Assert.False(draft.TryCommit());
        }
        var changes = new List<SettingsColorsChangedEventArgs>();
        model.Changed += (_, args) => changes.Add(args);
        model.ResetColorsCommand.Execute(null);

        var change = Assert.Single(changes);
        var defaults = new WorkbenchPreferences();
        Assert.Equal(defaults.AccentColor, change.AccentColor);
        Assert.Equal(defaults.AudioGraph, change.AudioGraph);
        Assert.Equal(defaults.TimelineClips, change.TimelineClips);
        Assert.All(Drafts(model).Append(model.AccentDraft).Append(model.HighDraft), draft =>
        {
            Assert.False(draft.HasError);
            Assert.False(draft.IsDirty);
        });
    }

    private static ColorDraft[] Drafts(ColorsSettingsViewModel model)
    {
        return
        [
            model.SelectedClipDraft, model.InactiveClipDraft, model.StartLineDraft,
            model.EndLineDraft, model.SelectedRangeFillDraft, model.InactiveRangeFillDraft
        ];
    }

    private static TimelineClipPalette WithColor(TimelineClipPalette palette, int field, string value)
    {
        return field switch
        {
            0 => palette with { SelectedClip = value },
            1 => palette with { InactiveClip = value },
            2 => palette with { StartLine = value },
            3 => palette with { EndLine = value },
            4 => palette with { SelectedRangeFill = value },
            5 => palette with { InactiveRangeFill = value },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
    }

    private static string Color(TimelineClipPalette palette, int field)
    {
        return field switch
        {
            0 => palette.SelectedClip,
            1 => palette.InactiveClip,
            2 => palette.StartLine,
            3 => palette.EndLine,
            4 => palette.SelectedRangeFill,
            5 => palette.InactiveRangeFill,
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
    }
}
