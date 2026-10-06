using System.Globalization;
using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Panels.Masks;

internal sealed record MaskPointListItem(Guid Id, int Index, ScenePoint Position)
{
    public string Label => string.Create(CultureInfo.CurrentCulture, $"{Index}   ({Position.X:0.###}, {Position.Y:0.###})");
}
