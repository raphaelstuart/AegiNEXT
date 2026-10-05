using AegiNext.Desktop.Settings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace AegiNext.Desktop.Controls;

/// <summary>独立音频配色示意，复用时间线的颜色映射，不访问媒体或工程服务。</summary>
public sealed class AudioGraphPreviewControl : Control
{
    public static readonly StyledProperty<AudioGraphPalette> PaletteProperty =
        AvaloniaProperty.Register<AudioGraphPreviewControl, AudioGraphPalette>(nameof(Palette), new());
    private Color[] ramp = AudioGraphColorRamp.Create(new());
    private Pen waveform = new(new SolidColorBrush(AudioGraphColorRamp.Parse(new AudioGraphPalette().Waveform)), 2);

    /// <summary>随当前主题更新内置配色的呈现，保留自定义输入色值。</summary>
    public AudioGraphPreviewControl()
    {
        ActualThemeVariantChanged += (_, _) => RefreshColors();
        RefreshColors();
    }

    public AudioGraphPalette Palette
    {
        get => GetValue(PaletteProperty);
        set => SetValue(PaletteProperty, value);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var step = Bounds.Width / ramp.Length;
        for (var index = 0; index < ramp.Length; index++)
        {
            context.DrawRectangle(new SolidColorBrush(ramp[index]), null, new(index * step, 0, step + 1, Bounds.Height));
        }

        var previous = new Point(0, Bounds.Height / 2);
        for (var x = 1; x <= Bounds.Width; x++)
        {
            var phase = x / Bounds.Width;
            var point = new Point(x, Bounds.Height / 2 + Math.Sin(phase * Math.PI * 14) * Math.Sin(phase * Math.PI) * Bounds.Height * 0.35);
            context.DrawLine(waveform, previous, point);
            previous = point;
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PaletteProperty)
        {
            RefreshColors();
        }
    }

    private void RefreshColors()
    {
        var colors = AudioGraphPalettes.Resolve(Palette, ActualThemeVariant != ThemeVariant.Dark);
        ramp = AudioGraphColorRamp.Create(colors);
        waveform = new(new SolidColorBrush(AudioGraphColorRamp.Parse(colors.Waveform)), 2);
        InvalidateVisual();
    }
}
