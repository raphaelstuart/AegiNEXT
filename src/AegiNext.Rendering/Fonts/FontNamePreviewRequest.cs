using AegiNext.Core.Projects;

namespace AegiNext.Rendering.Fonts;

/// <summary>字体菜单名称的完整字体身份及 DIP 排版参数，渲染缩放将 DIP 转换为物理像素。</summary>
public readonly record struct FontNamePreviewRequest(
    string FamilyName,
    SubtitleFontVariant? Variant,
    string Text,
    double FontSize,
    double RenderScale,
    double MaxWidth)
{
    private const int MAXIMUM_NAME_LENGTH = 512;

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(FamilyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(Text);
        if (FamilyName.Length > MAXIMUM_NAME_LENGTH || Text.Length > MAXIMUM_NAME_LENGTH)
        {
            throw new ArgumentException("预览名称必须位于字体目录允许的长度范围内。");
        }

        ValidatePositiveFinite(FontSize, nameof(FontSize));
        ValidatePositiveFinite(RenderScale, nameof(RenderScale));
        ValidatePositiveFinite(MaxWidth, nameof(MaxWidth));
    }

    private static void ValidatePositiveFinite(double value, string parameter)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameter, value, "预览尺寸与渲染缩放必须为有限正数。");
        }
    }
}
