using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Media;
using AegiNext.Media.Decoding;

namespace AegiNext.Desktop.Tests;

public sealed class PreviewDecodePreferencesTests
{
    [Fact]
    public async Task MissingDecodePreferenceDefaultsToAutoWithoutRewritingOldSettings()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var path = Path.Combine(directory.Path, "preferences.json");
        const string JSON = "{\"Language\":\"zh-CN\",\"Volume\":0.375}";
        await File.WriteAllTextAsync(path, JSON, CancellationToken.None);
        using var store = new WorkbenchPreferencesStore(directory.Path);

        var preferences = store.Load();

        Assert.Null(store.LoadError);
        Assert.Equal(VideoDecodeMode.Auto, preferences.PreviewDecodeMode);
        Assert.Equal(0.375f, preferences.Volume);
        Assert.Equal(JSON, await File.ReadAllTextAsync(path, CancellationToken.None));
    }

    [Theory]
    [InlineData(VideoDecodeMode.Auto)]
    [InlineData(VideoDecodeMode.Software)]
    [InlineData(VideoDecodeMode.Hardware)]
    public async Task DecodeModePersistsWithOtherPreferences(VideoDecodeMode mode)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var store = new WorkbenchPreferencesStore(directory.Path);
        var expected = new WorkbenchPreferences { PreviewDecodeMode = mode, Volume = 0.25f, Language = "zh-CN" };

        await store.SaveAsync(expected, CancellationToken.None);

        Assert.Equal(expected, store.Load());
        Assert.NotEqual(expected, expected with { PreviewDecodeMode = (VideoDecodeMode)(((int)mode + 1) % 3) });
    }

    [Fact]
    public void InvalidDecodeModeIsRejectedBeforeSaving()
    {
        Assert.Throws<InvalidDataException>(() => new WorkbenchPreferences { PreviewDecodeMode = (VideoDecodeMode)99 }.Validate());
    }

    [Fact]
    public void LanguageSynchronizationAndRollbackDoNotEmitNewDecodeRequests()
    {
        var model = new MediaSettingsViewModel(new() { PreviewDecodeMode = VideoDecodeMode.Software });
        var requests = new List<VideoDecodeMode>();
        model.DecodeModeChanged += (_, value) => requests.Add(value.Mode);
        model.PropertyChanged += (_, value) =>
        {
            if (value.PropertyName == nameof(MediaSettingsViewModel.DecodeModes))
            {
                model.SelectedDecodeMode = model.DecodeModes[0];
            }
        };

        model.RefreshLanguage();

        Assert.Equal(VideoDecodeMode.Software, model.SelectedDecodeMode!.Mode);
        Assert.Empty(requests);
        model.SelectedDecodeMode = model.DecodeModes.Single(value => value.Mode == VideoDecodeMode.Hardware);
        Assert.Equal(VideoDecodeMode.Hardware, Assert.Single(requests));
        model.IsBusy = true;
        model.SelectedDecodeMode = model.DecodeModes[0];
        model.UpdatePreferences(new() { PreviewDecodeMode = VideoDecodeMode.Software, Volume = 0.5f });
        Assert.Equal(VideoDecodeMode.Hardware, model.SelectedDecodeMode.Mode);
        Assert.False(model.CanChangeDecodeMode);
        model.IsBusy = false;
        model.UpdatePreferences(new() { PreviewDecodeMode = VideoDecodeMode.Software, Volume = 0.5f });
        Assert.Equal(VideoDecodeMode.Software, model.SelectedDecodeMode.Mode);
        Assert.Single(requests);
    }
}
