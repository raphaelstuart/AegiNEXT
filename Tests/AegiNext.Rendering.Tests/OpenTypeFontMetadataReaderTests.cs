using System.Buffers.Binary;
using System.Text;
using AegiNext.Rendering.Fonts;
using SkiaSharp;

namespace AegiNext.Rendering.Tests;

public sealed class OpenTypeFontMetadataReaderTests
{
    [Fact]
    public void PinnedVariableFixturePreservesDesignerNamesCoordinatesAndPostScriptNames()
    {
        using var typeface = SKTypeface.FromFile(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf"));
        var metadata = OpenTypeFontMetadataReader.Read(typeface);
        Assert.Equal("Noto Sans", metadata.FamilyName);
        Assert.Equal("Regular", metadata.SubfamilyName);
        Assert.Equal(2, metadata.Axes.Length);
        Assert.Equal(9, metadata.NamedInstances.Length);
        var semiBold = Assert.Single(metadata.NamedInstances, instance => instance.Name == "SemiBold");
        Assert.Equal(6, semiBold.Index);
        Assert.Equal("NotoSans-SemiBold", semiBold.PostScriptName);
        Assert.Equal(600, semiBold.Coordinates[OpenTypeFontMetadataReader.WEIGHT_TAG]);
        Assert.Equal(100, semiBold.Coordinates[OpenTypeFontMetadataReader.WIDTH_TAG]);
        Assert.Equal(900, metadata.NamedInstances[^1].Coordinates[OpenTypeFontMetadataReader.WEIGHT_TAG]);
    }

    [Fact]
    public void OrdinaryFontWithNoVariationTableRemainsValid()
    {
        var metadata = OpenTypeFontMetadataReader.Read(NameTable((1, "Example"), (2, "Book"), (6, "Example-Book")), [], Os2Table(437, 5));
        Assert.Equal("Example", metadata.FamilyName);
        Assert.Equal(437, metadata.Weight);
        Assert.Equal(5, metadata.Width);
        Assert.Empty(metadata.Axes);
        Assert.Empty(metadata.NamedInstances);
    }

    [Fact]
    public void TypographicFamilyCollectsLegacyAndLocalizedAliases()
    {
        var names = NameTable((1, "Example Heavy"), (2, "Regular"), (16, "Example"), (17, "Heavy"), (16, "示例"));
        BinaryPrimitives.WriteUInt16BigEndian(names.AsSpan(6 + 4 * 12 + 4), 0x0804);
        var metadata = OpenTypeFontMetadataReader.Read(names, [], []);
        Assert.Equal("Example", metadata.FamilyName);
        Assert.Equal("Heavy", metadata.SubfamilyName);
        Assert.Contains("Example Heavy", metadata.FamilyAliases);
        Assert.Contains("Example", metadata.FamilyAliases);
        Assert.Contains("示例", metadata.FamilyAliases);
    }

    [Fact]
    public void CustomNamedWeightDoesNotBecomeAStandardPreset()
    {
        var metadata = OpenTypeFontMetadataReader.Read(NameTable((1, "Example"), (2, "Regular"), (256, "Text")), VariationTable(437), []);
        var instance = Assert.Single(metadata.NamedInstances);
        Assert.Equal("Text", instance.Name);
        Assert.Equal(437, instance.Coordinates[OpenTypeFontMetadataReader.WEIGHT_TAG]);
        Assert.Null(instance.PostScriptName);
    }

    [Fact]
    public void ZeroAxisVariationTableIsTreatedAsNonVariable()
    {
        var variation = VariationTable(437);
        BinaryPrimitives.WriteUInt16BigEndian(variation.AsSpan(8), 0);
        var metadata = OpenTypeFontMetadataReader.Read(NameTable((1, "Example")), variation, []);
        Assert.Empty(metadata.Axes);
        Assert.Empty(metadata.NamedInstances);
    }

    [Fact]
    public void NamingTableRejectsTruncatedRecordsAndInvalidStringStorage()
    {
        Assert.Throws<InvalidDataException>(() => OpenTypeFontMetadataReader.Read([0, 0, 0, 1, 0, 6], [], []));
        var names = NameTable((1, "Example"));
        BinaryPrimitives.WriteUInt16BigEndian(names.AsSpan(4), 6);
        Assert.Throws<InvalidDataException>(() => OpenTypeFontMetadataReader.Read(names, [], []));
        names = NameTable((1, "Example"));
        BinaryPrimitives.WriteUInt16BigEndian(names.AsSpan(14), 3);
        Assert.Throws<InvalidDataException>(() => OpenTypeFontMetadataReader.Read(names, [], []));
        names = NameTable((1, "Example"));
        BinaryPrimitives.WriteUInt16BigEndian(names.AsSpan(18), 0xD800);
        Assert.Throws<InvalidDataException>(() => OpenTypeFontMetadataReader.Read(names, [], []));
    }

    [Fact]
    public void OversizedTableIsRejectedBeforeRecordParsing()
    {
        Assert.Throws<InvalidDataException>(() => OpenTypeFontMetadataReader.Read(new byte[4 * 1024 * 1024 + 1], [], []));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(4 * 1024 * 1024 + 1)]
    public void NativeTableSizesRejectNegativeAndOversizedValues(int size)
    {
        Assert.Throws<InvalidDataException>(() => OpenTypeFontMetadataReader.ValidateNativeTableSize(size));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(6)]
    [InlineData(16)]
    [InlineData(17)]
    public void SelectedFontIdentifiersRejectControlsAndExcessiveLength(ushort nameId)
    {
        Assert.Throws<InvalidDataException>(() => OpenTypeFontMetadataReader.Read(NameTable((nameId, "Invalid\nName")), [], []));
        Assert.Throws<InvalidDataException>(() => OpenTypeFontMetadataReader.Read(NameTable((nameId, new string('A', 513))), [], []));
    }

    [Theory]
    [InlineData(4, 65535)]
    [InlineData(10, 19)]
    [InlineData(12, 65535)]
    [InlineData(14, 7)]
    public void VariationTableRejectsInvalidOffsetsCountsAndRecordSizes(int offset, ushort value)
    {
        var variation = VariationTable(437);
        BinaryPrimitives.WriteUInt16BigEndian(variation.AsSpan(offset), value);
        Assert.Throws<InvalidDataException>(() => OpenTypeFontMetadataReader.Read(NameTable((1, "Example"), (256, "Text")), variation, []));
    }

    [Fact]
    public void VariationTableRejectsInvalidAxisRangesAndOutOfRangeInstance()
    {
        var names = NameTable((1, "Example"), (256, "Text"));
        var variation = VariationTable(437);
        BinaryPrimitives.WriteInt32BigEndian(variation.AsSpan(24), 1001 << 16);
        Assert.Throws<InvalidDataException>(() => OpenTypeFontMetadataReader.Read(names, variation, []));
        variation = VariationTable(437);
        BinaryPrimitives.WriteInt32BigEndian(variation.AsSpan(40), 1001 << 16);
        Assert.Throws<InvalidDataException>(() => OpenTypeFontMetadataReader.Read(names, variation, []));
        Assert.Throws<InvalidDataException>(() => OpenTypeFontMetadataReader.Read(NameTable((1, "Example")), VariationTable(437), []));
    }

    [Fact]
    public void Os2TableRejectsTruncationAndOutOfRangeStyleValues()
    {
        Assert.Throws<InvalidDataException>(() => OpenTypeFontMetadataReader.Read([], [], new byte[63]));
        Assert.Throws<InvalidDataException>(() => OpenTypeFontMetadataReader.Read([], [], Os2Table(0, 5)));
        Assert.Throws<InvalidDataException>(() => OpenTypeFontMetadataReader.Read([], [], Os2Table(400, 10)));
    }

    private static byte[] NameTable(params (ushort NameId, string Value)[] names)
    {
        var encoded = names.Select(name => Encoding.BigEndianUnicode.GetBytes(name.Value)).ToArray();
        var storageOffset = 6 + names.Length * 12;
        var data = new byte[storageOffset + encoded.Sum(bytes => bytes.Length)];
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), (ushort)names.Length);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4), (ushort)storageOffset);
        var stringOffset = 0;
        for (var index = 0; index < names.Length; index++)
        {
            var record = data.AsSpan(6 + index * 12);
            BinaryPrimitives.WriteUInt16BigEndian(record, 3);
            BinaryPrimitives.WriteUInt16BigEndian(record[2..], 1);
            BinaryPrimitives.WriteUInt16BigEndian(record[4..], 0x0409);
            BinaryPrimitives.WriteUInt16BigEndian(record[6..], names[index].NameId);
            BinaryPrimitives.WriteUInt16BigEndian(record[8..], (ushort)encoded[index].Length);
            BinaryPrimitives.WriteUInt16BigEndian(record[10..], (ushort)stringOffset);
            encoded[index].CopyTo(data, storageOffset + stringOffset);
            stringOffset += encoded[index].Length;
        }
        return data;
    }

    private static byte[] VariationTable(int weight)
    {
        var data = new byte[44];
        BinaryPrimitives.WriteUInt16BigEndian(data, 1);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4), 16);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(6), 2);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(10), 20);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(14), 8);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16), OpenTypeFontMetadataReader.WEIGHT_TAG);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(20), 100 << 16);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(24), 400 << 16);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(28), 900 << 16);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(34), 257);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(36), 256);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(40), weight << 16);
        return data;
    }

    private static byte[] Os2Table(ushort weight, ushort width)
    {
        var data = new byte[78];
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4), weight);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(6), width);
        return data;
    }
}
