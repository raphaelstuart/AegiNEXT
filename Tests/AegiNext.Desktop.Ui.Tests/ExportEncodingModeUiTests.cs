using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Media.Encoding;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class ExportEncodingModeUiTests
{
    [AvaloniaFact]
    public async Task HardwareToggleShowsBitratePreservesOptionsAndExportsImmutableModeSnapshot()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        context.Layouts.Activate(WorkbenchPanelIds.EXPORT);
        context.Flush();
        var view = context.Panels[WorkbenchPanelIds.EXPORT];
        var vm = context.Session.ViewModel.Export;
        var toggle = view.FindControl<CheckBox>("HardwareEncoderToggle")!;
        var crf = view.FindControl<NumericDraftInput>("CrfInput")!;
        var bitrate = view.FindControl<NumericDraftInput>("VideoBitrateInput")!;
        Assert.False(vm.UseHardwareEncoder);
        Assert.True(crf.IsEffectivelyVisible);
        Assert.False(bitrate.IsEffectivelyVisible);
        toggle.IsChecked = true;
        bitrate.RawText = "12.5";
        crf.RawText = "pending CPU quality";
        context.Flush();
        Assert.True(vm.UseHardwareEncoder);
        Assert.False(crf.IsEffectivelyVisible);
        Assert.True(bitrate.IsEffectivelyVisible);
        Assert.True(await context.Layouts.ApplyPresetAsync(WorkspaceLayoutPresets.TIMING));
        Assert.True(await context.Layouts.ApplyPresetAsync(WorkspaceLayoutPresets.ENCODE));
        context.Flush();
        Assert.True(vm.UseHardwareEncoder);
        Assert.Equal("12.5", vm.VideoBitrateText);
        Assert.Equal("pending CPU quality", vm.CrfText);
        context.Dialogs.ResolveOutput(context.OutputPath);
        var operation = context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        var request = await context.ExportService.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(VideoEncodingMode.HARDWARE, request.EncodingMode);
        Assert.Equal(12500000, request.VideoBitrate);
        Assert.Equal(20, request.Crf);
        context.ExportService.Report(new(3, new(1), 0.5, "encoding", "h264_videotoolbox"));
        context.Flush();
        Assert.Contains("h264_videotoolbox", vm.Status, StringComparison.Ordinal);
        Assert.Contains(context.Session.Journal.Entries, entry => entry.Summary.Contains("h264_videotoolbox", StringComparison.Ordinal));
        context.ExportService.Release();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(vm.IsRunning);
    }

    [AvaloniaFact]
    public async Task InvalidActiveBitrateBlocksExportButHiddenBitrateDoesNotBlockCpu()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        var vm = context.Session.ViewModel.Export;
        vm.UseHardwareEncoder = true;
        vm.VideoBitrateText = "invalid";
        await context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        Assert.Equal(0, context.Dialogs.SaveRequests);
        Assert.Equal("VideoBitrateInput", context.Session.ViewModel.InvalidFieldKey);
        Assert.Equal("invalid", vm.VideoBitrateText);
        vm.UseHardwareEncoder = false;
        context.Dialogs.ResolveOutput(context.OutputPath);
        var operation = context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        var request = await context.ExportService.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(VideoEncodingMode.SOFTWARE, request.EncodingMode);
        Assert.Equal(8000000, request.VideoBitrate);
        Assert.Equal("invalid", vm.VideoBitrateText);
        context.ExportService.Release();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
