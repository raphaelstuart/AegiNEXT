using System.Collections.Immutable;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Media;
using AegiNext.Media.Audio;
using AegiNext.Media.Playback;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private ImmutableArray<AudioDeviceCalibration> appliedAudioCalibrations = [];
    private readonly Lock audioCalibrationGate = new();
    private Task audioCalibrationCompletion = Task.CompletedTask;
    private bool switchingAudioDevice;
    private AudioOutputClockSnapshot? lastAudioClock;
    private Exception? recoveredAudioError;

    internal event EventHandler? AudioClockChanged;
    internal AudioOutputClockSnapshot? AudioClock => controller.AudioClock;
    internal bool IsSwitchingAudioDevice => switchingAudioDevice;
    internal Task AudioCalibrationCompletion => audioCalibrationCompletion;

    private void InitializeAudioCalibration()
    {
        appliedAudioCalibrations = preferences.AudioCalibrations;
        controller.ConfigureAudioCalibration(clock =>
        {
            lock (audioCalibrationGate)
            {
                return appliedAudioCalibrations.FirstOrDefault(value => value.Matches(clock))?.Delay ?? MediaTime.Zero;
            }
        });
    }

    private void QueueAudioCalibrationPreferences(WorkbenchPreferences previous, WorkbenchPreferences value)
    {
        if (previous.AudioCalibrations.AsSpan().SequenceEqual(value.AudioCalibrations.AsSpan()))
        {
            return;
        }
        var preceding = audioCalibrationCompletion;
        audioCalibrationCompletion = RunCommandAsync(async () =>
        {
            await preceding;
            if (!closing)
            {
                await ApplyAudioCalibrationPreferencesAsync(value.AudioCalibrations);
            }
        });
    }

    private async Task ApplyAudioCalibrationPreferencesAsync(ImmutableArray<AudioDeviceCalibration> profiles)
    {
        if (AudioClock is null)
        {
            SetAppliedAudioCalibrations(profiles);
            AudioClockChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        var snapshot = controller.Snapshot;
        var resume = snapshot.State == VideoPlaybackState.PLAYING || snapshot.AudioAuditionActive;
        var target = snapshot.Position;
        switchingAudioDevice = true;
        playback.Invalidate();
        ViewModel.CancelGestures();
        InvalidateTimingSession();
        AudioClockChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await controller.PauseAsync();
            SetAppliedAudioCalibrations(profiles);
            if (controller.Snapshot.FilePath == snapshot.FilePath && !closing)
            {
                if (AudioClock?.Quality == AudioClockQuality.UNAVAILABLE)
                {
                    await controller.ReopenAudioOutputAsync(projectOperationsCancellation.Token);
                }
                else
                {
                    await controller.SeekAsync(target);
                }
                if (resume)
                {
                    await controller.PlayAsync();
                }
            }
        }
        finally
        {
            switchingAudioDevice = false;
            if (!closing)
            {
                AudioClockChanged?.Invoke(this, EventArgs.Empty);
                Tick();
            }
        }
    }

    internal async Task SetAudioCalibrationAsync(AudioDeviceCalibration calibration)
    {
        calibration.Validate();
        if (AudioClock is not { Quality: not AudioClockQuality.UNAVAILABLE } clock || !calibration.Matches(clock))
        {
            throw new InvalidOperationException("校准目标已不是当前音频输出设备。");
        }
        UpdatePreferences(value => value with
        {
            AudioCalibrations = value.AudioCalibrations.Where(profile => !profile.Matches(clock)).Append(calibration).ToImmutableArray()
        });
        await audioCalibrationCompletion;
    }

    private void RefreshAudioClockStatus()
    {
        var clock = AudioClock;
        var previous = lastAudioClock;
        lastAudioClock = clock;
        if (previous?.DeviceId != clock?.DeviceId || previous?.Backend != clock?.Backend || previous?.SampleRate != clock?.SampleRate ||
            previous?.Channels != clock?.Channels || previous?.Quality != clock?.Quality)
        {
            AudioClockChanged?.Invoke(this, EventArgs.Empty);
        }
        var error = controller.Snapshot.AudioError;
        if (!closing && !projectBusy && !switchingAudioDevice && clock?.Quality == AudioClockQuality.UNAVAILABLE &&
            error is not null && !ReferenceEquals(error, recoveredAudioError) && audioCalibrationCompletion.IsCompleted)
        {
            recoveredAudioError = error;
            audioCalibrationCompletion = RunCommandAsync(RebuildAudioDeviceAsync);
        }
    }

    private async Task RebuildAudioDeviceAsync()
    {
        var snapshot = controller.Snapshot;
        var resume = snapshot.State == VideoPlaybackState.PLAYING;
        switchingAudioDevice = true;
        playback.Invalidate();
        ViewModel.CancelGestures();
        InvalidateTimingSession();
        AudioClockChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await controller.ReopenAudioOutputAsync(projectOperationsCancellation.Token);
            if (resume && !closing)
            {
                await controller.PlayAsync();
            }
        }
        finally
        {
            switchingAudioDevice = false;
            if (!closing)
            {
                AudioClockChanged?.Invoke(this, EventArgs.Empty);
                Tick();
            }
        }
    }

    private void SetAppliedAudioCalibrations(ImmutableArray<AudioDeviceCalibration> profiles)
    {
        lock (audioCalibrationGate)
        {
            appliedAudioCalibrations = profiles;
        }
    }
}
