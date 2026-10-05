using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using AegiNext.Core.Projects;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Editing;

internal sealed class SubtitleRow : INotifyPropertyChanged
{
    private string startText;
    private string endText;
    private string text;
    private SubtitleLine original;

    internal SubtitleRow(SubtitleLine line, int number)
    {
        Id = line.Id;
        original = line;
        Number = number;
        startText = TimelineTimeText.Format(line.Start);
        endText = TimelineTimeText.Format(line.End);
        text = line.Text;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; }

    public int Number { get; }

    public string ContentType => Localization.Get("Workbench.SubtitleType." + original.ContentKind);

    public string Duration => ((double)(original.End - original.Start).Numerator / (original.End - original.Start).Denominator).ToString("0.000", CultureInfo.InvariantCulture);

    internal SubtitleLine Original => original;

    internal bool IsDirty => Text != original.Text || StartText != TimelineTimeText.Format(original.Start) ||
                             EndText != TimelineTimeText.Format(original.End);

    internal void Accept(SubtitleLine line)
    {
        original = line;
        StartText = TimelineTimeText.Format(line.Start);
        EndText = TimelineTimeText.Format(line.End);
        Text = line.Text;
        PropertyChanged?.Invoke(this, new(nameof(Duration)));
        PropertyChanged?.Invoke(this, new(nameof(ContentType)));
    }

    public string StartText
    {
        get => startText;
        set => Set(ref startText, value);
    }

    public string EndText
    {
        get => endText;
        set => Set(ref endText, value);
    }

    public string Text
    {
        get => text;
        set => Set(ref text, value);
    }

    internal SubtitleLine CreateEditedLine(SubtitleLine original, ProjectDocument? document = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (original.Id != Id)
        {
            throw new ArgumentException("字幕草稿与原始行不匹配。", nameof(original));
        }

        var start = StartText == TimelineTimeText.Format(original.Start) ? original.Start : TimelineTimeText.Parse(StartText);
        var end = EndText == TimelineTimeText.Format(original.End) ? original.End : TimelineTimeText.Parse(EndText);
        if (start >= end)
        {
            throw new InvalidDataException("结束时间必须晚于开始时间。");
        }

        if (start == original.Start && end == original.End && Text == original.Text)
        {
            return original;
        }
        var source = document ?? new ProjectDocument
        {
            Subtitles = [original], Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = original.Id,
                Start = original.Start, End = original.End }]
        };
        var edited = SubtitleTextDifference.Apply(source, original, Text);
        return edited with { Start = start, End = end };
    }

    internal void RefreshLanguage() => PropertyChanged?.Invoke(this, new(nameof(ContentType)));

    private void Set(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new(propertyName));
    }
}
