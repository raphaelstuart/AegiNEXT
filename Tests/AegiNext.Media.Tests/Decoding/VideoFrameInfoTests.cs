using AegiNext.Core.Media;
using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;

namespace AegiNext.Media.Tests.Decoding;

public sealed class VideoFrameInfoTests
{
    [Fact]
    public void MissingFlagsNeverPromoteNumericFieldsToKnownMetadata()
    {
        var value = CreateFrame();
        value.pts = -90000;
        value.bestEffortTimestamp = -1000;
        value.duration = 90000;
        value.colorTransfer = 777;
        var hdr = new NativeDecodedHdrInfo
        {
            maxLuminance = new() { numerator = 1000, denominator = 1 },
            maxContentLightLevel = 1000
        };

        var info = new VideoFrameInfo(value, hdr, []);

        Assert.Null(info.PresentationTimestampValue);
        Assert.Null(info.BestEffortTimestampValue);
        Assert.Null(info.PresentationTimestamp);
        Assert.Null(info.BestEffortTimestamp);
        Assert.Null(info.DurationTicks);
        Assert.Null(info.MasteringDisplay);
        Assert.Null(info.ContentLight);
        Assert.Equal(777, info.ColorTransferCode);
        Assert.Null(info.Color.Transfer);
        Assert.False(info.Color.IsPq);
        Assert.False(info.Color.IsHlg);
    }

    [Fact]
    public void NegativePresentationAndEstimatedTimestampsKeepTheirIndependentTimeBases()
    {
        var value = CreateFrame();
        value.flags = 3;
        value.pts = -90000;
        value.bestEffortTimestamp = -1000;

        var info = new VideoFrameInfo(value, default, []);

        Assert.Equal(-90000, info.PresentationTimestampValue);
        Assert.Equal(-1000, info.BestEffortTimestampValue);
        Assert.Equal(new MediaTimestamp(-90000, new(1, 90000)), info.PresentationTimestamp);
        Assert.Equal(new MediaTimestamp(-1000, new(1, 1000)), info.BestEffortTimestamp);
        Assert.Equal(new MediaTime(-1), info.PresentationTimestamp!.ToMediaTime());
        Assert.Equal(info.PresentationTimestamp.ToMediaTime(), info.BestEffortTimestamp!.ToMediaTime());
        Assert.Null(info.RawFrameTimeBase);
        Assert.Equal(0, info.RawFrameTimeBaseNumerator);
        Assert.Equal(1, info.RawFrameTimeBaseDenominator);
    }

    [Fact]
    public void MissingTimeBasesPreserveRawValuesWithoutInventingSeconds()
    {
        var value = CreateFrame();
        value.flags = 3;
        value.pts = 42;
        value.bestEffortTimestamp = 43;
        value.timeBaseNum = 0;
        value.streamTimeBaseDen = 0;

        var info = new VideoFrameInfo(value, default, []);

        Assert.Equal(42, info.PresentationTimestampValue);
        Assert.Equal(43, info.BestEffortTimestampValue);
        Assert.Null(info.TimeBase);
        Assert.Null(info.StreamTimeBase);
        Assert.Null(info.PresentationTimestamp);
        Assert.Null(info.BestEffortTimestamp);
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    public void RejectsAvNoPtsMarkedAsAKnownTimestamp(uint flags)
    {
        var value = CreateFrame();
        value.flags = flags;
        value.pts = long.MinValue;
        value.bestEffortTimestamp = long.MinValue;

        Assert.Throws<InvalidDataException>(() => new VideoFrameInfo(value, default, []));
    }

    [Theory]
    [InlineData(0u, 9000L, null)]
    [InlineData(4u, 0L, null)]
    [InlineData(4u, -1L, null)]
    [InlineData(4u, 9000L, 9000L)]
    public void DurationRequiresBothItsPresenceFlagAndAPositiveValue(uint flags, long duration, long? expected)
    {
        var value = CreateFrame();
        value.flags = flags;
        value.duration = duration;

        var info = new VideoFrameInfo(value, default, []);

        Assert.Equal(expected, info.DurationTicks);
    }

    [Fact]
    public void PrimariesOnlyMasteringDoesNotReadAbsentLuminance()
    {
        var hdr = new NativeDecodedHdrInfo
        {
            flags = 3,
            redX = new() { numerator = 34000, denominator = 50000 },
            whitePointX = new() { numerator = 15635, denominator = 50000 },
            minLuminance = new() { numerator = 1, denominator = 0 },
            maxLuminance = new() { numerator = 1000, denominator = 0 }
        };

        var info = new VideoFrameInfo(CreateFrame(), hdr, ["Mastering display metadata"]);

        var mastering = Assert.IsType<MediaMasteringDisplayInfo>(info.MasteringDisplay);
        Assert.Equal(new MediaRatio(17, 25), mastering.RedX);
        Assert.Equal(new MediaRatio(3127, 10000), mastering.WhitePointX);
        Assert.Null(mastering.MinLuminance);
        Assert.Null(mastering.MaxLuminance);
        Assert.Null(info.ContentLight);
        Assert.Equal("Mastering display metadata", Assert.Single(info.SideDataTypes));
    }

    [Fact]
    public void LuminanceOnlyMasteringKeepsExactZeroAndLeavesPrimariesUnknown()
    {
        var hdr = new NativeDecodedHdrInfo
        {
            flags = 5,
            redX = new() { numerator = 1, denominator = 0 },
            minLuminance = new() { numerator = 0, denominator = 10000 },
            maxLuminance = new() { numerator = 10000000, denominator = 10000 }
        };

        var info = new VideoFrameInfo(CreateFrame(), hdr, []);

        var mastering = Assert.IsType<MediaMasteringDisplayInfo>(info.MasteringDisplay);
        Assert.Null(mastering.RedX);
        Assert.Null(mastering.WhitePointX);
        Assert.Equal(new MediaRatio(0, 1), mastering.MinLuminance);
        Assert.Equal(new MediaRatio(1000, 1), mastering.MaxLuminance);
    }

    [Fact]
    public void PresentContentLightTreatsZeroAsUnknownIndependentlyPerField()
    {
        var hdr = new NativeDecodedHdrInfo { flags = 8, maxFrameAverageLightLevel = 400 };

        var info = new VideoFrameInfo(CreateFrame(), hdr, []);

        var light = Assert.IsType<MediaContentLightInfo>(info.ContentLight);
        Assert.Null(light.MaxContentLightLevel);
        Assert.Equal(400u, light.MaxFrameAverageLightLevel);
        Assert.Null(info.MasteringDisplay);
    }

    [Fact]
    public void RejectsAnInvalidRationalWhenItsMasteringFieldIsPresent()
    {
        var hdr = new NativeDecodedHdrInfo { flags = 5, maxLuminance = new() { numerator = 1000, denominator = 0 } };

        Assert.Throws<InvalidDataException>(() => new VideoFrameInfo(CreateFrame(), hdr, []));
    }

    [Fact]
    public void FrameFlagsAndDecodeErrorsAreIndependentSnapshotFacts()
    {
        var value = CreateFrame();
        value.flags = 8 | 16 | 32 | 64;
        value.decodeErrorFlags = 5;
        var info = new VideoFrameInfo(value, default, []);
        value.flags = 0;
        value.decodeErrorFlags = 0;

        Assert.True(info.IsKeyFrame);
        Assert.True(info.IsCorrupt);
        Assert.True(info.IsInterlaced);
        Assert.True(info.IsTopFieldFirst);
        Assert.Equal(5u, info.DecodeErrorFlags);
        Assert.Null(info.PresentationTimestamp);
    }

    [Fact]
    public void RejectsCroppingThatRemovesTheEntireFrame()
    {
        var value = CreateFrame();
        value.cropLeft = 32;
        value.cropRight = 32;

        Assert.Throws<InvalidDataException>(() => new VideoFrameInfo(value, default, []));
    }

    private static NativeDecodedFrameInfo CreateFrame()
    {
        return new()
        {
            width = 64,
            height = 48,
            planeCount = 3,
            componentCount = 3,
            timeBaseNum = 1,
            timeBaseDen = 90000,
            rawFrameTimeBaseNum = 0,
            rawFrameTimeBaseDen = 1,
            streamTimeBaseNum = 1,
            streamTimeBaseDen = 1000
        };
    }
}
