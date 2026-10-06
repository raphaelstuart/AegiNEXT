using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Editing;

internal sealed class SubtitleDetailsStyleDraft : ObservableObject
{
    private readonly HashSet<string> dirty = [];
    private readonly Dictionary<string, SubtitleInlineStyleOverride> previewValues = [];
    private bool loading;
    private string fontFamily = string.Empty;
    private string fontSizeText = string.Empty;
    private string strokeWidthText = string.Empty;
    private string shadowXText = string.Empty;
    private string shadowYText = string.Empty;
    private string shadowBlurText = string.Empty;
    private SubtitleStyle source = new();
    private readonly SubtitleFontSelectionService fonts;
    private FontSelection? selectedFont;

    internal SubtitleDetailsStyleDraft(SubtitleFontSelectionService? fonts = null)
    {
        this.fonts = fonts ?? new(Array.Empty<AegiNext.Rendering.Fonts.SystemFontFace>());
        Fill.Changed += (_, _) => Mark(nameof(Fill));
        Stroke.Changed += (_, _) => Mark(nameof(Stroke));
        Shadow.Changed += (_, _) => Mark(nameof(Shadow));
    }

    internal event EventHandler? Changed;
    internal bool IsDirty => dirty.Count > 0;
    internal IEnumerable<string> DirtyFields => dirty.ToArray();
    internal bool IsFieldDirty(string field) => dirty.Contains(field);
    internal string? InvalidField { get; private set; }
    public ColorDraft Fill { get; } = new();
    public ColorDraft Stroke { get; } = new(SceneColor.Black);
    public ColorDraft Shadow { get; } = new(SceneColor.Black);
    public string FontFamily
    {
        get => fontFamily;
        set
        {
            if (SetProperty(ref fontFamily, value))
            {
                selectedFont = null;
                Mark(nameof(FontFamily));
            }
        }
    }
    public string FontSizeText
    {
        get => fontSizeText;
        set
        {
            if (SetProperty(ref fontSizeText, value))
            {
                Mark(nameof(FontSizeText));
            }
        }
    }
    public string StrokeWidthText
    {
        get => strokeWidthText;
        set
        {
            if (SetProperty(ref strokeWidthText, value))
            {
                Mark(nameof(StrokeWidthText));
            }
        }
    }
    public string ShadowXText
    {
        get => shadowXText;
        set
        {
            if (SetProperty(ref shadowXText, value))
            {
                Mark(nameof(ShadowXText));
            }
        }
    }
    public string ShadowYText
    {
        get => shadowYText;
        set
        {
            if (SetProperty(ref shadowYText, value))
            {
                Mark(nameof(ShadowYText));
            }
        }
    }
    public string ShadowBlurText
    {
        get => shadowBlurText;
        set
        {
            if (SetProperty(ref shadowBlurText, value))
            {
                Mark(nameof(ShadowBlurText));
            }
        }
    }

    private void Mark(string field)
    {
        if (!loading)
        {
            InvalidField = null;
            dirty.Add(field);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    internal void Load(SubtitleStyle style)
    {
        source = style;
        loading = true;
        try
        {
            FontFamily = SubtitleFontSelectionService.FromStyle(style).DisplayName;
            selectedFont = null;
            FontSizeText = style.FontSize.ToString(CultureInfo.InvariantCulture);
            StrokeWidthText = style.StrokeWidth.ToString(CultureInfo.InvariantCulture);
            ShadowXText = style.ShadowOffset.X.ToString(CultureInfo.InvariantCulture);
            ShadowYText = style.ShadowOffset.Y.ToString(CultureInfo.InvariantCulture);
            ShadowBlurText = style.ShadowBlur.ToString(CultureInfo.InvariantCulture);
            Fill.Load(style.Fill);
            Stroke.Load(style.Stroke);
            Shadow.Load(style.ShadowColor);
            dirty.Clear();
            previewValues.Clear();
            InvalidField = null;
        }
        finally
        {
            loading = false;
        }
    }

    internal void RestoreField(string field)
    {
        loading = true;
        try
        {
            switch (field)
            {
                case nameof(FontFamily):
                    FontFamily = SubtitleFontSelectionService.FromStyle(source).DisplayName;
                    selectedFont = null;
                    break;
                case nameof(FontSizeText):
                    FontSizeText = source.FontSize.ToString(CultureInfo.InvariantCulture);
                    break;
                case nameof(StrokeWidthText):
                    StrokeWidthText = source.StrokeWidth.ToString(CultureInfo.InvariantCulture);
                    break;
                case nameof(ShadowXText):
                    ShadowXText = source.ShadowOffset.X.ToString(CultureInfo.InvariantCulture);
                    break;
                case nameof(ShadowYText):
                    ShadowYText = source.ShadowOffset.Y.ToString(CultureInfo.InvariantCulture);
                    break;
                case nameof(ShadowBlurText):
                    ShadowBlurText = source.ShadowBlur.ToString(CultureInfo.InvariantCulture);
                    break;
                case nameof(Fill):
                    Fill.Load(source.Fill);
                    break;
                case nameof(Stroke):
                    Stroke.Load(source.Stroke);
                    break;
                case nameof(Shadow):
                    Shadow.Load(source.ShadowColor);
                    break;
            }
            dirty.Remove(field);
            previewValues.Remove(field);
            InvalidField = null;
        }
        finally
        {
            loading = false;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void AcceptField(string field, SubtitleStyle committed)
    {
        source = committed;
        RestoreField(field);
    }

    internal void SelectFont(FontSelection selection)
    {
        loading = true;
        try
        {
            FontFamily = selection.DisplayName;
            selectedFont = selection;
        }
        finally
        {
            loading = false;
        }
        Mark(nameof(FontFamily));
    }

    internal SubtitleInlineStyleOverride? ReadPreviewField(SubtitleStyle current, string field)
    {
        var previousFailure = InvalidField;
        try
        {
            var value = Read(current, field);
            previewValues[field] = value;
            return value;
        }
        catch (InvalidDataException)
        {
            return previewValues.GetValueOrDefault(field);
        }
        finally
        {
            InvalidField = previousFailure;
        }
    }

    internal SubtitleInlineStyleOverride ReadPreview(SubtitleStyle current)
    {
        var result = new SubtitleInlineStyleOverride();
        foreach (var field in DirtyFields)
        {
            var effective = result.ApplyTo(current);
            if (ReadPreviewField(effective, field) is not { } value)
            {
                continue;
            }
            if (value.ShadowOffset is { } offset)
            {
                value = value with
                {
                    ShadowOffset = field == nameof(ShadowXText)
                        ? new(offset.X, effective.ShadowOffset.Y)
                        : new(effective.ShadowOffset.X, offset.Y)
                };
            }
            result = result.Merge(value);
        }
        return result;
    }

    internal SubtitleInlineStyleOverride Read(SubtitleStyle current, string? onlyField = null)
    {
        InvalidField = null;
        bool Includes(string key) => dirty.Contains(key) && (onlyField is null || onlyField == key);
        if (Includes(nameof(FontFamily)) && string.IsNullOrWhiteSpace(FontFamily))
        {
            InvalidField = nameof(FontFamily);
            throw new InvalidDataException(Localization.Get("Settings.FontRequired"));
        }
        double? Number(string field, string text, double minimum, double maximum)
        {
            if (!Includes(field))
            {
                return null;
            }
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                !double.IsFinite(value) || value < minimum || value > maximum)
            {
                InvalidField = field;
                var label = field switch
                {
                    nameof(FontSizeText) => "FontSize", nameof(StrokeWidthText) => "StrokeWidth", nameof(ShadowXText) => "ShadowX",
                    nameof(ShadowYText) => "ShadowY", _ => "ShadowBlur"
                };
                throw new InvalidDataException(Localization.Get("Workbench." + label) + ": " +
                    minimum.ToString(CultureInfo.InvariantCulture) + " … " + maximum.ToString(CultureInfo.InvariantCulture));
            }
            return value;
        }
        SceneColor? Color(string field, ColorDraft value)
        {
            if (!Includes(field))
            {
                return null;
            }
            if (!value.TryCommit(out var parsed))
            {
                InvalidField = value.InvalidFieldKey is { } component ? field + "." + component : field;
                throw new InvalidDataException(value.Error);
            }
            return parsed;
        }
        var x = Number(nameof(ShadowXText), ShadowXText, -4096, 4096);
        var y = Number(nameof(ShadowYText), ShadowYText, -4096, 4096);
        var font = Includes(nameof(FontFamily))
            ? SubtitleFontSelectionService.CreateOverride(selectedFont ?? fonts.Resolve(FontFamily, current))
            : new SubtitleInlineStyleOverride();
        return font with
        {
            FontSize = Number(nameof(FontSizeText), FontSizeText, 0.01, 4096),
            StrokeWidth = Number(nameof(StrokeWidthText), StrokeWidthText, 0, 4096),
            Fill = Color(nameof(Fill), Fill), Stroke = Color(nameof(Stroke), Stroke),
            ShadowColor = Color(nameof(Shadow), Shadow),
            ShadowOffset = x.HasValue || y.HasValue ? new(x ?? current.ShadowOffset.X, y ?? current.ShadowOffset.Y) : null,
            ShadowBlur = Number(nameof(ShadowBlurText), ShadowBlurText, 0, 512)
        };
    }
}
