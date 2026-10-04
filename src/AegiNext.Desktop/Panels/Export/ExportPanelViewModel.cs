using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AegiNext.Desktop.Workspace;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Controls;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Media.Analysis;
using Avalonia.Media;

namespace AegiNext.Desktop.Panels.Export;

internal sealed class ExportPanelViewModel : ObservableObject
{
    private string crfText = "20";
    private string audioBitrateText = "192";
    private readonly WorkbenchSession session;
    private int codec;
    private string[] codecs = [];
    private int speed = 1;
    private string[] speeds = [];
    private decimal? crf = 20;
    private int audioMode;
    private string[] audioModes = [];
    private decimal? audioBitrate = 192;
    private bool isRunning;
    private bool progressVisible;
    private bool progressIndeterminate;
    private double progress;
    private string status = string.Empty;

    internal ExportPanelViewModel(WorkbenchSession session)
    {
        this.session = session;
        CancelEncodeCommand = new AsyncRelayCommand(() => session.RunCommandAsync(() => session.CancelExportAsync()));
    }

    public int Codec
    {
        get => codec;
        set => SetProperty(ref codec, value);
    }

    public string[] Codecs
    {
        get => codecs;
        set => SetProperty(ref codecs, value);
    }

    public int Speed
    {
        get => speed;
        set => SetProperty(ref speed, value);
    }

    public string[] Speeds
    {
        get => speeds;
        set => SetProperty(ref speeds, value);
    }

    public decimal? Crf
    {
        get => crf;
        set => SetProperty(ref crf, value);
    }

    public int AudioMode
    {
        get => audioMode;
        set => SetProperty(ref audioMode, value);
    }

    public string[] AudioModes
    {
        get => audioModes;
        set => SetProperty(ref audioModes, value);
    }

    public decimal? AudioBitrate
    {
        get => audioBitrate;
        set => SetProperty(ref audioBitrate, value);
    }

    public bool IsRunning
    {
        get => isRunning;
        set => SetProperty(ref isRunning, value);
    }

    public bool ProgressVisible
    {
        get => progressVisible;
        set => SetProperty(ref progressVisible, value);
    }

    public bool ProgressIndeterminate
    {
        get => progressIndeterminate;
        set => SetProperty(ref progressIndeterminate, value);
    }

    public double Progress
    {
        get => progress;
        set => SetProperty(ref progress, value);
    }

    public string Status
    {
        get => status;
        set => SetProperty(ref status, value);
    }

    internal void RefreshChoices(string[] codecOptions, string[] speedOptions, string[] audioOptions)
    {
        var selection = (Codec, Speed, AudioMode);
        Codecs = codecOptions;
        Speeds = speedOptions;
        AudioModes = audioOptions;
        codec = selection.Codec;
        speed = selection.Speed;
        audioMode = selection.AudioMode;
        OnPropertyChanged(nameof(Codec));
        OnPropertyChanged(nameof(Speed));
        OnPropertyChanged(nameof(AudioMode));
    }

    public ICommand EncodeCommand => session.ViewModel.GetCommand(AegiNext.Desktop.Shortcuts.WorkbenchCommand.EXPORT_VIDEO);

    public ICommand CancelEncodeCommand { get; }
    public string CrfText
    {
        get => crfText;
        set => SetProperty(ref crfText, value);
    }
    public string AudioBitrateText
    {
        get => audioBitrateText;
        set => SetProperty(ref audioBitrateText, value);
    }
}
