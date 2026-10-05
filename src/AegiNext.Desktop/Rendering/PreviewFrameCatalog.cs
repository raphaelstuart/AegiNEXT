using System.Runtime.CompilerServices;
using AegiNext.Media.Preview;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Rendering;

internal sealed class PreviewFrameCatalog
{
    private readonly ConditionalWeakTable<SdrVideoFrame, PreviewFrameRecord> backgrounds = new();

    internal void Register(SdrVideoFrame presented, SdrVideoFrame background, ProjectDocument? document = null,
        MediaTime? time = null, bool interactive = false, long qualityRevision = 0)
    {
        backgrounds.Add(presented, new(background, document, time, interactive, qualityRevision));
    }

    internal SdrVideoFrame? FindBackground(SdrVideoFrame presented)
    {
        return backgrounds.TryGetValue(presented, out var record) ? record.Background : null;
    }

    internal PreviewFrameRecord? FindIdentity(SdrVideoFrame presented) => backgrounds.TryGetValue(presented, out var record) ? record : null;

    internal void Clear()
    {
        backgrounds.Clear();
    }
}
