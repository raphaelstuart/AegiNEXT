namespace AegiNext.Rendering.Fonts;

/// <summary>持有本次解析的真实字体，指纹和绘制共享同一资源；实例限单线程使用。</summary>
public interface IFontNamePreviewFace : IDisposable
{
    string Fingerprint { get; }

    /// <summary>按指定名称和排版参数绘制透明字形；缺字时返回 null，不伪装为系统回退字体。</summary>
    FontNamePreview? Render(FontNamePreviewRequest request, CancellationToken token);
}
