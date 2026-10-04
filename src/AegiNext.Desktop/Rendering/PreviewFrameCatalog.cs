using System.Runtime.CompilerServices;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Rendering;

internal sealed class PreviewFrameCatalog
{
    private readonly ConditionalWeakTable<SdrVideoFrame, SdrVideoFrame> backgrounds = new();

    internal void Register(SdrVideoFrame presented, SdrVideoFrame background)
    {
        backgrounds.Add(presented, background);
    }

    internal SdrVideoFrame? FindBackground(SdrVideoFrame presented)
    {
        return backgrounds.TryGetValue(presented, out var background) ? background : null;
    }

    internal void Clear()
    {
        backgrounds.Clear();
    }
}
