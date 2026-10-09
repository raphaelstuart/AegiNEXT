namespace AegiNext.Rendering.Fonts;

/// <summary>在后台解析精确字体预览；不会为不可用的保存身份返回其他字体。</summary>
public interface IFontNamePreviewRenderer
{
    /// <summary>打开本次预览使用的真实字体，调用方负责释放；无法精确解析时返回 null。</summary>
    IFontNamePreviewFace? Resolve(FontNamePreviewRequest request, CancellationToken token);
}
