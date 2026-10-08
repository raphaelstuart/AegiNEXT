using System.Collections.Immutable;
using AegiNext.Application.Tasks;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Media;
using AegiNext.Media.Audio;

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
        if (closing)
        {
            return;
        }
        QueueAudioDeviceTask(new ApplyAudioCalibrationTask(this, value.AudioCalibrations));
    }

    private void QueueAudioDeviceTask(AegiTask task)
    {
        if (AegiTaskExecutionContext.Current is { } parent)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            audioCalibrationCompletion = completion.Task;
            parent.ScheduleAfterCompletion(task, handle =>
            {
                if (handle is null)
                {
                    completion.TrySetResult();
                    return;
                }
                _ = ObserveAudioDeviceTaskAsync(handle, completion);
            });
        }
        else
        {
            audioCalibrationCompletion = RunCommandAsync(() => applicationContext.Tasks.Submit(task).Completion);
        }
    }

    private async Task ObserveAudioDeviceTaskAsync(AegiTaskHandle handle, TaskCompletionSource completion)
    {
        try
        {
            await RunCommandAsync(() => handle.Completion);
        }
        finally
        {
            completion.TrySetResult();
        }
    }

    internal async Task ApplyAudioCalibrationPreferencesCoreAsync(ImmutableArray<AudioDeviceCalibration> profiles,
        AegiTaskExecutionContext context)
    {
        context.EnterCommit(() => !closing);
        if (AudioClock is null)
        {
            SetAppliedAudioCalibrations(profiles);
            AudioClockChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        switchingAudioDevice = true;
        playback.Invalidate();
        ViewModel.CancelGestures();
        InvalidateTimingSession();
        AudioClockChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            if (AudioClock?.Quality == AudioClockQuality.UNAVAILABLE)
            {
                SetAppliedAudioCalibrations(profiles);
                await controller.ReopenAudioOutputAsync(CancellationToken.None);
            }
            else
            {
                await controller.ApplyAudioCalibrationAsync(() => SetAppliedAudioCalibrations(profiles),
                    CancellationToken.None);
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
        if (!closing && !IsProjectBusy && !switchingAudioDevice && clock?.Quality == AudioClockQuality.UNAVAILABLE &&
            error is not null && !ReferenceEquals(error, recoveredAudioError) && audioCalibrationCompletion.IsCompleted)
        {
            recoveredAudioError = error;
            QueueAudioDeviceTask(new RebuildAudioOutputTask(this));
        }
    }

    internal async Task RebuildAudioDeviceCoreAsync(AegiTaskExecutionContext context)
    {
        var previousError = controller.Snapshot.AudioError;
        switchingAudioDevice = true;
        playback.Invalidate();
        ViewModel.CancelGestures();
        InvalidateTimingSession();
        AudioClockChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await controller.ReopenAudioOutputAsync(context.CancellationToken);
            if (previousError is not null && controller.Snapshot.AudioError is null)
            {
                DismissError(previousError);
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
