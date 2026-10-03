namespace AegiNext.Media;

/// <summary>
/// 原生呈现时实际读取的显示状态；名义白标度与 headroom 不代表经测量的物理亮度。
/// </summary>
public sealed record HdrPreviewStatus
{
    internal HdrPreviewStatus(NativeHdrStatus status)
    {
        DrawableWidth = status.drawableWidth;
        DrawableHeight = status.drawableHeight;
        Float16Verified = status.float16Verified != 0;
        EdrEnabled = status.edrEnabled != 0;
        DisplayId = status.displayId;
        CurrentHeadroom = status.currentHeadroom;
        PotentialHeadroom = status.potentialHeadroom;
        BackingScale = status.backingScale;
        NominalDisplayWhite = status.nominalDisplayWhite;
        SubmittedFrames = status.submittedFrames;
        SourceWhiteNits = status.sourceWhiteNits;
        SourcePeakNits = status.sourcePeakNits;
        LiveContexts = status.liveContexts;
    }

    public uint DrawableWidth { get; }
    public uint DrawableHeight { get; }
    public bool Float16Verified { get; }
    public bool EdrEnabled { get; }
    public uint DisplayId { get; }
    public float CurrentHeadroom { get; }
    public float PotentialHeadroom { get; }
    public float BackingScale { get; }
    public float NominalDisplayWhite { get; }
    public ulong SubmittedFrames { get; }
    public float SourceWhiteNits { get; }
    public float SourcePeakNits { get; }
    public uint LiveContexts { get; }
}
