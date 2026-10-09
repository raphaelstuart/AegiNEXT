using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Editing;

internal sealed class SubtitleKaraokeStyleDraft : ObservableObject
{
    private readonly SubtitleDetailsStyleDraft visual = new();

    internal SubtitleKaraokeStyleDraft()
    {
        visual.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        visual.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);
    }

    internal event EventHandler? Changed;
    internal bool IsDirty => visual.IsDirty;
    internal string? InvalidField => visual.InvalidField;
    internal bool IsFieldDirty(string field) => visual.IsFieldDirty(field);
    public ColorDraft Fill => visual.Fill;
    public ColorDraft Stroke => visual.Stroke;
    public ColorDraft Shadow => visual.Shadow;
    public string StrokeWidthText
    {
        get => visual.StrokeWidthText;
        set => visual.StrokeWidthText = value;
    }
    public string ShadowXText
    {
        get => visual.ShadowXText;
        set => visual.ShadowXText = value;
    }
    public string ShadowYText
    {
        get => visual.ShadowYText;
        set => visual.ShadowYText = value;
    }
    public string ShadowBlurText
    {
        get => visual.ShadowBlurText;
        set => visual.ShadowBlurText = value;
    }

    public string FillBlurText
    {
        get => visual.FillBlurText;
        set => visual.FillBlurText = value;
    }

    public string StrokeBlurText
    {
        get => visual.StrokeBlurText;
        set => visual.StrokeBlurText = value;
    }

    internal void Load(KaraokeHighlightStyle style) => visual.Load(AsSubtitleStyle(style));

    internal void RestoreField(string field) => visual.RestoreField(field);

    internal void AcceptField(string field, KaraokeHighlightStyle committed) => visual.AcceptField(field, AsSubtitleStyle(committed));

    internal KaraokeVisualStyleEdit ReadOverride(SubtitleStyle current, string? field = null)
    {
        return Convert(visual.Read(current, field),
            visual.IsFieldDirty(nameof(ShadowXText)) && (field is null || field == nameof(ShadowXText)),
            visual.IsFieldDirty(nameof(ShadowYText)) && (field is null || field == nameof(ShadowYText)));
    }

    internal KaraokeVisualStyleEdit ReadPreview(SubtitleStyle current)
    {
        var result = new KaraokeVisualStyleEdit();
        foreach (var field in visual.DirtyFields)
        {
            if (visual.ReadPreviewField(current, field) is { } value)
            {
                result = result.Merge(Convert(value, field == nameof(ShadowXText), field == nameof(ShadowYText)));
            }
        }
        return result;
    }

    internal KaraokeHighlightStyle Read(KaraokeHighlightStyle current)
    {
        var changed = visual.Read(AsSubtitleStyle(current));
        return current with
        {
            FillBlur = changed.FillBlur ?? current.FillBlur,
            StrokeBlur = changed.StrokeBlur ?? current.StrokeBlur,
            Fill = changed.Fill ?? current.Fill,
            Stroke = changed.Stroke ?? current.Stroke,
            StrokeWidth = changed.StrokeWidth ?? current.StrokeWidth,
            ShadowColor = changed.ShadowColor ?? current.ShadowColor,
            ShadowOffset = changed.ShadowOffset ?? current.ShadowOffset,
            ShadowBlur = changed.ShadowBlur ?? current.ShadowBlur
        };
    }

    private static KaraokeVisualStyleEdit Convert(SubtitleInlineStyleOverride value, bool shadowX, bool shadowY) => new()
    {
        FillBlur = value.FillBlur,
        StrokeBlur = value.StrokeBlur,
        Fill = value.Fill, Stroke = value.Stroke, StrokeWidth = value.StrokeWidth,
        ShadowColor = value.ShadowColor, ShadowBlur = value.ShadowBlur,
        ShadowX = shadowX ? value.ShadowOffset?.X : null,
        ShadowY = shadowY ? value.ShadowOffset?.Y : null
    };

    private static SubtitleStyle AsSubtitleStyle(KaraokeHighlightStyle value) => new()
    {
        FillBlur = value.FillBlur,
        StrokeBlur = value.StrokeBlur,
        Fill = value.Fill,
        Stroke = value.Stroke,
        StrokeWidth = value.StrokeWidth,
        ShadowColor = value.ShadowColor,
        ShadowOffset = value.ShadowOffset,
        ShadowBlur = value.ShadowBlur
    };
}
