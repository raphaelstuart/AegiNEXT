using System.Text.Json;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Colors;

namespace AegiNext.Desktop.Tests;

public sealed class SettingsPaletteTests
{
    [Fact]
    public void RestoringDefaultColorsCommitsBothColorsOnceAndClearsInvalidDrafts()
    {
        var original = new WorkbenchPreferences { AccentColor = "#B54880", AudioGraph = AudioGraphPalettes.Get(2) };
        var model = new ColorsSettingsViewModel(original);
        model.AccentDraft.HexText = "#bad";
        model.HighDraft.HexText = "#bad";
        Assert.False(model.AccentDraft.TryCommit());
        Assert.False(model.HighDraft.TryCommit());
        var changes = new List<SettingsColorsChangedEventArgs>();
        model.Changed += (_, args) => changes.Add(args);
        model.ResetColorsCommand.Execute(null);
        var value = Assert.Single(changes);
        var defaults = new WorkbenchPreferences();
        Assert.Equal(defaults.AccentColor, value.AccentColor);
        Assert.Equal(defaults.AudioGraph, value.AudioGraph);
        Assert.False(model.AccentDraft.HasError);
        Assert.False(model.HighDraft.HasError);
        Assert.False(model.AccentDraft.IsDirty);
        Assert.False(model.HighDraft.IsDirty);
        Assert.Equal(0, model.SchemeIndex);
    }

    [Fact]
    public void PaletteChoicesRetainIdentityThroughLanguageAndAppliedPreferenceRefresh()
    {
        var model = new ColorsSettingsViewModel(new());
        var choices = model.Schemes;
        model.RefreshLanguage();
        Assert.Same(choices, model.Schemes);
        model.SchemeIndex = 1;
        model.UpdatePreferences(new() { AudioGraph = model.AudioGraph });
        Assert.Same(choices, model.Schemes);
        model.SchemeIndex = 0;
        Assert.Equal(new AudioGraphPalette(), model.AudioGraph);
        model.SchemeIndex = 1;
        model.SchemeIndex = AudioGraphPalettes.CUSTOM_INDEX;
        Assert.Equal(AudioGraphPalettes.CUSTOM_INDEX, model.SchemeIndex);
        Assert.False(model.AudioGraph.AdaptToTheme);
        model.SchemeIndex = 0;
        Assert.Equal(new AudioGraphPalette(), model.AudioGraph);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void BuiltinPalettesAdaptToLightWithoutChangingPreferencesAndCustomColorsStayExact(int index)
    {
        var original = AudioGraphPalettes.Get(index);
        Assert.Same(original, AudioGraphPalettes.Resolve(original, false));
        var light = AudioGraphPalettes.Resolve(original, true);
        light.Validate();
        var colors = AudioGraphColorRamp.Create(light);
        Assert.True(colors[0].R + colors[0].G + colors[0].B > 700);
        Assert.True(colors[255].R + colors[255].G + colors[255].B < 400);
        Assert.Equal(AudioGraphPalettes.Get(index), original);
        var custom = original with { AdaptToTheme = false };
        Assert.Same(custom, AudioGraphPalettes.Resolve(custom, true));
        Assert.Equal(custom, JsonSerializer.Deserialize<AudioGraphPalette>(JsonSerializer.Serialize(custom)));
    }

    [Fact]
    public async Task PersonalPaletteRoundTripsAndOldPreferencesKeepClassicColors()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var store = new WorkbenchPreferencesStore(directory.Path);
        var path = Path.Combine(directory.Path, "preferences.json");
        await File.WriteAllTextAsync(path, """{"Version":1,"Language":"zh-CN","AccentColor":"#C54885"}""");
        var previous = store.Load();
        Assert.Null(store.LoadError);
        Assert.Equal(new AudioGraphPalette(), previous.AudioGraph);
        var expected = previous with { AudioGraph = AudioGraphPalettes.Get(1) with { Waveform = "#01020380" } };
        await store.SaveAsync(expected);
        Assert.Equal(expected, store.Load());
        Assert.Equal(expected, JsonSerializer.Deserialize<WorkbenchPreferences>(JsonSerializer.Serialize(expected)));
        Assert.NotEqual(expected, expected with { AudioGraph = new() });
        Assert.Single(Directory.GetFiles(directory.Path));
    }

    [Theory]
    [InlineData("#bad")]
    [InlineData("#01020380")]
    [InlineData("#GGFFFF")]
    public void InvalidSpectrumColorCannotBeStored(string value)
    {
        Assert.Throws<InvalidDataException>(() => new WorkbenchPreferences
        {
            AudioGraph = new() { High = value }
        }.Validate());
    }

    [Fact]
    public void PaletteDraftsApplyOnlyTheirCommittedFieldAndKeepInvalidInputOnRefresh()
    {
        var model = new ColorsSettingsViewModel(new());
        var changes = new List<SettingsColorsChangedEventArgs>();
        model.Changed += (_, args) => changes.Add(args);
        model.LowDraft.HexText = "#bad";
        Assert.False(model.LowDraft.TryCommit());
        model.WaveformDraft.HexText = "#01020380";
        Assert.True(model.WaveformDraft.TryCommit());
        var wave = Assert.Single(changes);
        Assert.Equal("#01020380", wave.AudioGraph.Waveform);
        Assert.True(wave.AudioGraph.UseClassicSpectrum);
        model.UpdatePreferences(new() { AudioGraph = wave.AudioGraph, Theme = WorkbenchTheme.DARK });
        model.RefreshLanguage();
        Assert.Equal("#bad", model.LowDraft.HexText);
        Assert.NotNull(model.LowDraft.Error);
        model.SchemeIndex = 1;
        Assert.Single(changes);
        model.LowDraft.Restore("HexText");
        model.SchemeIndex = 1;
        Assert.Equal(AudioGraphPalettes.Get(1), changes[^1].AudioGraph);
        Assert.Equal(2, changes.Count);
        Assert.False(model.LowDraft.IsDirty);
        Assert.False(model.WaveformDraft.IsDirty);
    }

    [Fact]
    public void ClassicRenderingKeepsExistingMapAndCustomRampUsesItsStops()
    {
        var classic = AudioGraphColorRamp.Create(new());
        foreach (var level in new[] { 0, 63, 127, 191, 255 })
        {
            var intensity = level / 255d;
            Assert.Equal((byte)Math.Clamp(18 + Math.Pow(intensity, 3) * 237, 0, 255), classic[level].R);
            Assert.Equal((byte)Math.Clamp(28 + intensity * intensity * 220, 0, 255), classic[level].G);
            Assert.Equal((byte)Math.Clamp(22 + 150 * Math.Sin(intensity * Math.PI), 0, 255), classic[level].B);
        }
        var palette = new AudioGraphPalette { UseClassicSpectrum = false, Low = "#000000", Mid = "#80A0C0", High = "#FFFFFF" };
        var ramp = AudioGraphColorRamp.Create(palette);
        Assert.Equal(0, ramp[0].R);
        Assert.InRange(ramp[128].G, 159, 161);
        Assert.Equal(255, ramp[255].R);
        Assert.All(ramp, color => Assert.Equal(255, color.A));
    }
}
