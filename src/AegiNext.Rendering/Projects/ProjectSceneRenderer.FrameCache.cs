using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Rendering.Projects;

public sealed partial class ProjectSceneRenderer
{
    private LinearRenderSurface? frameSurface;
    private ImmutableArray<EvaluatedLayer> frameLayers = [];
    private bool frameValid;

    /// <summary>
    /// 复用渲染器拥有的全尺寸 F16 帧，并始终完整复制预乘线性 float 像素；返回本次是否重新绘制。
    /// </summary>
    public bool CopyCachedFramePixels(ProjectDocument document, MediaTime time, Span<float> destination,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(document);
        if (destination.Length < checked(document.Width * document.Height * 4))
        {
            throw new ArgumentException("目标缓冲不足以容纳全部像素。", nameof(destination));
        }

        cancellationToken.ThrowIfCancellationRequested();
        Prepare(document);
        frameSurface ??= new(new(document.Width, document.Height, (float)document.ReferenceWhiteNits));
        var layers = SceneEvaluator.Evaluate(prepared!, time);
        var redrawn = !frameValid || !Equivalent(frameLayers, layers);
        if (redrawn)
        {
            frameValid = false;
            frameSurface.Clear();
            foreach (var layer in layers)
            {
                DrawLayer(document, frameSurface.Canvas, layer, cancellationToken: cancellationToken);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        frameSurface.CopyPixels(destination);
        cancellationToken.ThrowIfCancellationRequested();
        if (redrawn)
        {
            frameLayers = layers;
            frameValid = true;
        }

        return redrawn;
    }

    private void ClearFrameCache()
    {
        frameSurface?.Dispose();
        frameSurface = null;
        frameLayers = [];
        frameValid = false;
    }
}
