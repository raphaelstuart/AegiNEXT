using AegiNext.Rendering.Fonts;

namespace AegiNext.Desktop.Controls;

/// <summary>异步提供以实际字体绘制的菜单名称；控件不持有字体目录或磁盘缓存。</summary>
public interface IFontNamePreviewProvider
{
    /// <summary>取得透明文字蒙版；字体不可用时返回空值，取消仅影响当前调用方。</summary>
    ValueTask<FontNamePreview?> GetPreviewAsync(FontNamePreviewRequest request, CancellationToken cancellationToken);
}
