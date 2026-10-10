using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace AegiNext.Desktop.Settings.ColorTags;

/// <summary>个人颜色标签的独立名称与 RGB 草稿。</summary>
public sealed class SubtitleColorTagSettingsItem : ObservableObject, IDisposable
{
    private string name;

    /// <summary>从已提交标签建立可编辑草稿。</summary>
    public SubtitleColorTagSettingsItem(SubtitleColorTag tag)
    {
        SubtitleColorTagValidator.Validate(tag);
        Id = tag.Id;
        name = tag.Name;
        _ = ColorHexCodec.TryParse(tag.ColorHex, 1, false, out var color);
        ColorDraft = new(color) { IsAlphaEnabled = false };
        ColorDraft.Changed += OnColorChanged;
    }

    public Guid Id { get; }
    public ColorDraft ColorDraft { get; }
    public string ColorHex => ColorHexCodec.Format(ColorDraft.Value, false);

    [AllowNull]
    public string Name
    {
        get => name;
        set => SetProperty(ref name, value ?? string.Empty);
    }

    private void OnColorChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(ColorHex));
    }

    /// <summary>解除局部颜色草稿的状态订阅。</summary>
    public void Dispose()
    {
        ColorDraft.Changed -= OnColorChanged;
    }
}
