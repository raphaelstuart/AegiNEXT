using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Rendering.Projects;

namespace AegiNext.Desktop.Rendering;

internal sealed class EmbeddedPresetFontResolver(Guid assetId, EmbeddedSubtitleFont font) : IProjectAssetResolver
{
    public Stream Open(ProjectAsset asset)
    {
        if (asset.Id != assetId || asset.Kind != ProjectAssetKind.FONT)
        {
            throw new InvalidDataException("模板预览只能读取当前模板携带的字体。");
        }

        return new MemoryStream(font.Data.ToArray(), false);
    }
}
