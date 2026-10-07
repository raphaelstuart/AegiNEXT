using System.Collections.Immutable;
using System.Text.Json;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Media;
using AegiNext.Media.Audio;

namespace AegiNext.Desktop.Tests;

public sealed class AudioCalibrationPreferencesTests
{
    [Fact]
    public void LegacyPreferencesRemainUncalibratedAndProfilesSurviveRoundTrip()
    {
        var legacy = JsonSerializer.Deserialize<WorkbenchPreferences>("{\"Version\":1}")!;
        legacy.Validate();
        Assert.Empty(legacy.AudioCalibrations);
        var calibration = new AudioDeviceCalibration("speaker", "CoreAudio", 48000, 2, 85);
        var original = legacy with { AudioCalibrations = [calibration] };
        var restored = JsonSerializer.Deserialize<WorkbenchPreferences>(JsonSerializer.Serialize(original))!;
        restored.Validate();
        Assert.Equal(original, restored);
        Assert.Equal(calibration, Assert.Single(restored.AudioCalibrations));
    }

    [Theory]
    [InlineData("speaker", "CoreAudio", 48000, 2, true)]
    [InlineData("headphones", "CoreAudio", 48000, 2, false)]
    [InlineData("speaker", "WASAPI", 48000, 2, false)]
    [InlineData("speaker", "CoreAudio", 44100, 2, false)]
    [InlineData("speaker", "CoreAudio", 48000, 6, false)]
    public void CalibrationRequiresTheSameDeviceBackendAndOutputFormat(string device, string backend,
        int rate, int channels, bool expected)
    {
        var profile = new AudioDeviceCalibration("speaker", "CoreAudio", 48000, 2, 85);
        Assert.Equal(expected, profile.Matches(new(0, 0, 1, device, backend, 1, AudioClockQuality.SYSTEM, 0, rate, channels)));
    }

    [Fact]
    public void DuplicateAndOutOfRangeProfilesAreRejected()
    {
        var profile = new AudioDeviceCalibration("speaker", "CoreAudio", 48000, 2, 0);
        Assert.Throws<InvalidDataException>(() => new WorkbenchPreferences { AudioCalibrations = [profile, profile] }.Validate());
        Assert.Throws<InvalidDataException>(() => new WorkbenchPreferences { AudioCalibrations = [profile with { ExtraDelayMilliseconds = 1001 }] }.Validate());
        Assert.Throws<InvalidDataException>(() => new WorkbenchPreferences { AudioCalibrations = default(ImmutableArray<AudioDeviceCalibration>) }.Validate());
    }

    [Fact]
    public void InvalidCalibrationDraftAndDeviceReplacementCannotCommitToTheOldDevice()
    {
        var preferences = new WorkbenchPreferences();
        var model = new MediaSettingsViewModel(preferences);
        var requests = new List<AudioDeviceCalibration>();
        model.AudioCalibrationChanged += (_, request) => requests.Add(request.Calibration);
        model.UpdateAudioStatus(new(0, 0, 1, "speaker", "CoreAudio", 0, AudioClockQuality.SYSTEM, 0));
        model.ExtraDelayMillisecondsText = "invalid";
        Assert.False(model.CommitAudioCalibration());
        model.UpdatePreferences(preferences with { Volume = 0.5F });
        Assert.Equal("invalid", model.ExtraDelayMillisecondsText);
        model.UpdateAudioStatus(new(0, 0, 1, "headphones", "CoreAudio", 1, AudioClockQuality.SYSTEM, 0));
        Assert.Equal("0", model.ExtraDelayMillisecondsText);
        model.ExtraDelayMillisecondsText = "50";
        Assert.True(model.CommitAudioCalibration());
        Assert.Equal("headphones", Assert.Single(requests).DeviceId);
        Assert.Equal(50, Assert.Single(requests).ExtraDelayMilliseconds);
    }
}
