using System.Collections.Immutable;
using AegiNext.Core.Media;
using AegiNext.Core.Timing;

namespace AegiNext.Media.Decoding;

/// <summary>
/// 实际解码或硬件下载帧的事实快照；帧级色彩不由流级信息补写，估算时间戳与原始 PTS 分开保存。
/// CPU 与 GPU 后端可能交付不同像素布局、编码尺寸及裁剪矩形；可视区域定义为尺寸减去裁剪，
/// 后端不会补造已由硬件移除的编码边缘，NV12／P010 保留其实际平面及位深。
/// </summary>
public sealed record VideoFrameInfo
{
    internal unsafe VideoFrameInfo(NativeDecodedFrameInfo value, NativeDecodedHdrInfo hdr, ImmutableArray<string> sideDataTypes)
    {
        if (value.width == 0 || value.height == 0 || value.planeCount is 0 or > 4 || value.componentCount is 0 or > 4)
        {
            throw new InvalidDataException("原生视频帧的尺寸或分量数无效。");
        }

        Width = checked((int)value.width);
        Height = checked((int)value.height);
        PlaneCount = checked((int)value.planeCount);
        PixelFormatId = value.pixelFormat;
        PixelFormat = NativeDecodeError.ReadText(new(value.pixelFormatName, NativeDecodeMethods.NAME_CAPACITY));
        TimeBase = ReadTimeBase(value.timeBaseNum, value.timeBaseDen);
        RawFrameTimeBase = ReadTimeBase(value.rawFrameTimeBaseNum, value.rawFrameTimeBaseDen);
        StreamTimeBase = ReadTimeBase(value.streamTimeBaseNum, value.streamTimeBaseDen);
        RawFrameTimeBaseNumerator = value.rawFrameTimeBaseNum;
        RawFrameTimeBaseDenominator = value.rawFrameTimeBaseDen;
        PresentationTimestampValue = ReadTimestampValue(value.pts, (value.flags & 1) != 0);
        BestEffortTimestampValue = ReadTimestampValue(value.bestEffortTimestamp, (value.flags & 2) != 0);
        PresentationTimestamp = PresentationTimestampValue is { } pts && TimeBase is { } timeBase ? new(pts, timeBase) : null;
        BestEffortTimestamp = BestEffortTimestampValue is { } estimate && StreamTimeBase is { } streamBase ? new(estimate, streamBase) : null;
        DurationTicks = (value.flags & 4) != 0 && value.duration > 0 ? value.duration : null;
        IsKeyFrame = (value.flags & 8) != 0;
        IsCorrupt = (value.flags & 16) != 0;
        IsInterlaced = (value.flags & 32) != 0;
        IsTopFieldFirst = (value.flags & 64) != 0;
        DecodeErrorFlags = value.decodeErrorFlags;
        SampleAspectRatio = value.sampleAspectRatioNum > 0 && value.sampleAspectRatioDen > 0
            ? new(value.sampleAspectRatioNum, value.sampleAspectRatioDen)
            : null;
        CropLeft = checked((int)value.cropLeft);
        CropTop = checked((int)value.cropTop);
        CropRight = checked((int)value.cropRight);
        CropBottom = checked((int)value.cropBottom);
        if ((long)CropLeft + CropRight >= Width || (long)CropTop + CropBottom >= Height)
        {
            throw new InvalidDataException("原生视频帧裁剪超出画面范围。");
        }

        var depths = ImmutableArray.CreateBuilder<int>(checked((int)value.componentCount));
        for (var index = 0; index < value.componentCount; index++)
        {
            depths.Add(checked((int)value.componentDepth[index]));
        }

        ComponentDepths = depths.ToImmutable();
        Color = new()
        {
            Range = ReadOptionalName(value.colorRangeName),
            Matrix = ReadOptionalName(value.colorMatrixName),
            Primaries = ReadOptionalName(value.colorPrimariesName),
            Transfer = ReadOptionalName(value.colorTransferName),
            ChromaLocation = ReadOptionalName(value.chromaLocationName)
        };
        ColorRangeCode = value.colorRange;
        ColorMatrixCode = value.colorMatrix;
        ColorPrimariesCode = value.colorPrimaries;
        ColorTransferCode = value.colorTransfer;
        ChromaLocationCode = value.chromaLocation;
        AlphaModeCode = value.alphaMode;
        AlphaMode = ReadOptionalName(value.alphaModeName);
        SideDataTypes = sideDataTypes;
        MasteringDisplay = ReadMastering(hdr);
        ContentLight = (hdr.flags & 8) != 0 ? new()
        {
            MaxContentLightLevel = hdr.maxContentLightLevel == 0 ? null : hdr.maxContentLightLevel,
            MaxFrameAverageLightLevel = hdr.maxFrameAverageLightLevel == 0 ? null : hdr.maxFrameAverageLightLevel
        } : null;
    }

    public int Width { get; }

    public int Height { get; }

    public int PlaneCount { get; }

    public string PixelFormat { get; }

    public int PixelFormatId { get; }

    public MediaTimeBase? TimeBase { get; }

    public MediaTimeBase? RawFrameTimeBase { get; }

    public MediaTimeBase? StreamTimeBase { get; }

    public int RawFrameTimeBaseNumerator { get; }

    public int RawFrameTimeBaseDenominator { get; }

    public long? PresentationTimestampValue { get; }

    public long? BestEffortTimestampValue { get; }

    public MediaTimestamp? PresentationTimestamp { get; }

    public MediaTimestamp? BestEffortTimestamp { get; }

    public long? DurationTicks { get; }

    public bool IsKeyFrame { get; }

    public bool IsCorrupt { get; }

    public bool IsInterlaced { get; }

    public bool IsTopFieldFirst { get; }

    public uint DecodeErrorFlags { get; }

    public MediaRatio? SampleAspectRatio { get; }

    public int CropLeft { get; }

    public int CropTop { get; }

    public int CropRight { get; }

    public int CropBottom { get; }

    public ImmutableArray<int> ComponentDepths { get; }

    public MediaColorInfo Color { get; }

    public int ColorRangeCode { get; }

    public int ColorMatrixCode { get; }

    public int ColorPrimariesCode { get; }

    public int ColorTransferCode { get; }

    public int ChromaLocationCode { get; }

    public int AlphaModeCode { get; }

    public string? AlphaMode { get; }

    public MediaMasteringDisplayInfo? MasteringDisplay { get; }

    public MediaContentLightInfo? ContentLight { get; }

    public ImmutableArray<string> SideDataTypes { get; }

    private static MediaTimeBase? ReadTimeBase(int numerator, int denominator)
    {
        return numerator > 0 && denominator > 0 ? new(numerator, denominator) : null;
    }

    private static long? ReadTimestampValue(long value, bool present)
    {
        if (present && value == long.MinValue)
        {
            throw new InvalidDataException("原生帧将未定义时间戳标记为已知。");
        }

        return present ? value : null;
    }

    private static unsafe string? ReadOptionalName(byte* name)
    {
        var text = NativeDecodeError.ReadText(new(name, NativeDecodeMethods.NAME_CAPACITY));
        return text.Length > 0 ? text : null;
    }

    private static MediaMasteringDisplayInfo? ReadMastering(NativeDecodedHdrInfo value)
    {
        if ((value.flags & 1) == 0)
        {
            return null;
        }

        var primariesPresent = (value.flags & 2) != 0;
        var luminancePresent = (value.flags & 4) != 0;
        return new()
        {
            RedX = primariesPresent ? ReadRatio(value.redX) : null,
            RedY = primariesPresent ? ReadRatio(value.redY) : null,
            GreenX = primariesPresent ? ReadRatio(value.greenX) : null,
            GreenY = primariesPresent ? ReadRatio(value.greenY) : null,
            BlueX = primariesPresent ? ReadRatio(value.blueX) : null,
            BlueY = primariesPresent ? ReadRatio(value.blueY) : null,
            WhitePointX = primariesPresent ? ReadRatio(value.whitePointX) : null,
            WhitePointY = primariesPresent ? ReadRatio(value.whitePointY) : null,
            MinLuminance = luminancePresent ? ReadRatio(value.minLuminance) : null,
            MaxLuminance = luminancePresent ? ReadRatio(value.maxLuminance) : null
        };
    }

    private static MediaRatio? ReadRatio(NativeDecodeRatio value)
    {
        if (value.numerator == 0 && value.denominator == 0)
        {
            return null;
        }

        if (value.denominator <= 0)
        {
            throw new InvalidDataException("原生 HDR 元数据的有理数分母无效。");
        }

        return new(value.numerator, value.denominator);
    }
}
