using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using AegiNext.Core.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Fonts;

internal static class OpenTypeFontMetadataReader
{
    internal const uint NAME_TAG = 0x6E616D65;
    internal const uint VARIATIONS_TAG = 0x66766172;
    internal const uint OS2_TAG = 0x4F532F32;
    internal const uint WEIGHT_TAG = 0x77676874;
    internal const uint WIDTH_TAG = 0x77647468;
    internal const uint ITALIC_TAG = 0x6974616C;
    internal const uint SLANT_TAG = 0x736C6E74;
    private const int MAX_TABLE_BYTES = 4 * 1024 * 1024;
    private const string MAC_ROMAN_HIGH_CHARACTERS = "ÄÅÇÉÑÖÜáàâäãåçéèêëíìîïñóòôöõúùûü†°¢£§•¶ß®©™´¨≠ÆØ∞±≤≥¥µ∂∑∏π∫ªºΩæø¿¡¬√ƒ≈∆«»… ÀÃÕŒœ–—“”‘’÷◊ÿŸ⁄€‹›ﬁﬂ‡·‚„‰ÂÊÁËÈÍÎÏÌÓÔÒÚÛÙıˆ˜¯˘˙˚¸˝˛ˇ";
    private static readonly UnicodeEncoding unicode = new(true, false, true);

    internal static OpenTypeFontMetadata Read(SKTypeface typeface)
    {
        ArgumentNullException.ThrowIfNull(typeface);
        return Read(ReadTable(typeface, NAME_TAG), ReadTable(typeface, VARIATIONS_TAG), ReadTable(typeface, OS2_TAG));
    }

    internal static OpenTypeFontMetadata Read(ReadOnlySpan<byte> nameTable, ReadOnlySpan<byte> variationTable, ReadOnlySpan<byte> os2Table)
    {
        ValidateTableLength(nameTable, "name");
        ValidateTableLength(variationTable, "fvar");
        ValidateTableLength(os2Table, "OS/2");
        var names = ReadNames(nameTable);
        var metadata = new OpenTypeFontMetadata
        {
            FamilyName = Name(names, 16) ?? Name(names, 1),
            FamilyAliases = names.Where(name => name.NameId is 1 or 16 or 21)
                .Select(name => name.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToImmutableArray(),
            SubfamilyName = Name(names, 17) ?? Name(names, 2),
            PostScriptName = Name(names, 6)
        };
        metadata = ReadStyle(os2Table, metadata);
        metadata = ReadVariations(variationTable, names, metadata);
        foreach (var name in metadata.FamilyAliases.Select(name => (string?)name).Concat([metadata.FamilyName, metadata.SubfamilyName, metadata.PostScriptName])
                     .Concat(metadata.NamedInstances.SelectMany(instance => new[] { instance.Name, instance.PostScriptName })))
        {
            if (name is not null)
            {
                ValidateName(name);
            }
        }
        return metadata;
    }

    internal static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 512 || name.Any(char.IsControl))
        {
            throw new InvalidDataException("OpenType font identifier is empty, exceeds 512 characters or contains control characters.");
        }
        ProjectValidator.ValidateText(name);
    }

    internal static void ValidateNativeTableSize(int size)
    {
        if (size < 0 || size > MAX_TABLE_BYTES)
        {
            throw new InvalidDataException($"OpenType table size is outside 0..{MAX_TABLE_BYTES} bytes.");
        }
    }

    private static byte[] ReadTable(SKTypeface typeface, uint tag)
    {
        var size = typeface.GetTableSize(tag);
        ValidateNativeTableSize(size);
        if (size == 0)
        {
            return [];
        }
        if (!typeface.TryGetTableData(tag, out var data) || data.Length != size)
        {
            throw new InvalidDataException($"OpenType table 0x{tag:X8} could not be read in full.");
        }
        return data;
    }

    private static ImmutableArray<OpenTypeNameRecord> ReadNames(ReadOnlySpan<byte> table)
    {
        if (table.IsEmpty)
        {
            return [];
        }
        RequireRange(table, 0, 6, "name header");
        var version = UInt16(table, 0);
        if (version > 1)
        {
            throw new InvalidDataException("Unsupported OpenType name table version.");
        }
        var count = UInt16(table, 2);
        var storageOffset = UInt16(table, 4);
        var recordsEnd = 6 + count * 12;
        RequireRange(table, 6, count * 12, "name records");
        var languages = ImmutableArray<string>.Empty;
        if (version == 1)
        {
            RequireRange(table, recordsEnd, 2, "name language count");
            var languageCount = UInt16(table, recordsEnd);
            var languageRecordsOffset = recordsEnd + 2;
            RequireRange(table, languageRecordsOffset, languageCount * 4, "name language records");
            recordsEnd = languageRecordsOffset + languageCount * 4;
            var builder = ImmutableArray.CreateBuilder<string>(languageCount);
            for (var index = 0; index < languageCount; index++)
            {
                var offset = languageRecordsOffset + index * 4;
                var bytes = NameBytes(table, storageOffset, UInt16(table, offset + 2), UInt16(table, offset));
                builder.Add(DecodeUnicode(bytes));
            }
            languages = builder.MoveToImmutable();
        }
        if (storageOffset < recordsEnd || storageOffset > table.Length)
        {
            throw new InvalidDataException("OpenType name string storage overlaps records or exceeds the table.");
        }
        var records = ImmutableArray.CreateBuilder<OpenTypeNameRecord>();
        for (var index = 0; index < count; index++)
        {
            var offset = 6 + index * 12;
            var platform = UInt16(table, offset);
            var encoding = UInt16(table, offset + 2);
            var language = UInt16(table, offset + 4);
            var nameId = UInt16(table, offset + 6);
            var bytes = NameBytes(table, storageOffset, UInt16(table, offset + 10), UInt16(table, offset + 8));
            var isUnicode = platform == 0 || (platform == 3 && encoding is 0 or 1 or 10);
            var isMacRoman = platform == 1 && encoding == 0;
            if (!isUnicode && !isMacRoman)
            {
                continue;
            }
            if (language >= 0x8000 && (version != 1 || language - 0x8000 >= languages.Length))
            {
                continue;
            }
            var value = (isUnicode ? DecodeUnicode(bytes) : DecodeMacRoman(bytes)).Trim('\0', ' ');
            if (value.Length == 0)
            {
                continue;
            }
            var english = language >= 0x8000
                ? languages[language - 0x8000].StartsWith("en", StringComparison.OrdinalIgnoreCase)
                : (platform == 3 && (language & 0x03FF) == 9) || (platform == 1 && language == 0);
            var priority = english ? 100 : platform == 0 ? 80 : 10;
            if (platform == 3)
            {
                priority += 5;
            }
            records.Add(new(nameId, value, priority));
        }
        return records.ToImmutable();
    }

    private static OpenTypeFontMetadata ReadStyle(ReadOnlySpan<byte> table, OpenTypeFontMetadata metadata)
    {
        if (table.IsEmpty)
        {
            return metadata;
        }
        RequireRange(table, 0, 78, "OS/2 version 0 fields");
        var version = UInt16(table, 0);
        var minimumLength = version switch
        {
            0 => 78,
            1 => 86,
            <= 4 => 96,
            _ => 100
        };
        RequireRange(table, 0, minimumLength, "OS/2 version fields");
        var weight = UInt16(table, 4);
        var width = UInt16(table, 6);
        if (weight is < 1 or > 1000 || width is < 1 or > 9)
        {
            throw new InvalidDataException("OpenType OS/2 weight or width class is outside its supported range.");
        }
        var selection = UInt16(table, 62);
        return metadata with { Weight = weight, Width = width, Italic = (selection & 0x0201) != 0 };
    }

    private static OpenTypeFontMetadata ReadVariations(ReadOnlySpan<byte> table, ImmutableArray<OpenTypeNameRecord> names, OpenTypeFontMetadata metadata)
    {
        if (table.IsEmpty)
        {
            return metadata;
        }
        RequireRange(table, 0, 16, "fvar header");
        if (UInt16(table, 0) != 1)
        {
            throw new InvalidDataException("Unsupported OpenType fvar table version.");
        }
        var axesOffset = UInt16(table, 4);
        var axisCount = UInt16(table, 8);
        if (axisCount == 0)
        {
            return metadata;
        }
        var axisSize = UInt16(table, 10);
        var instanceCount = UInt16(table, 12);
        var instanceSize = UInt16(table, 14);
        var minimumInstanceSize = 4 + axisCount * 4;
        if (axesOffset < 16 || axisSize < 20 || instanceSize < minimumInstanceSize || instanceSize == minimumInstanceSize + 1)
        {
            throw new InvalidDataException("OpenType fvar record offset or size is invalid.");
        }
        RequireRange(table, axesOffset, (long)axisCount * axisSize, "fvar axes");
        var axes = ImmutableArray.CreateBuilder<OpenTypeVariationAxis>(axisCount);
        var seenTags = new HashSet<uint>();
        for (var index = 0; index < axisCount; index++)
        {
            var offset = axesOffset + index * axisSize;
            var tag = BinaryPrimitives.ReadUInt32BigEndian(table[offset..]);
            var minimum = Fixed(table, offset + 4);
            var defaultValue = Fixed(table, offset + 8);
            var maximum = Fixed(table, offset + 12);
            if (!seenTags.Add(tag) || minimum > defaultValue || defaultValue > maximum)
            {
                throw new InvalidDataException("OpenType fvar has a duplicate axis tag or an invalid coordinate range.");
            }
            axes.Add(new(tag, minimum, defaultValue, maximum));
        }
        var instancesOffset = axesOffset + axisCount * axisSize;
        RequireRange(table, instancesOffset, (long)instanceCount * instanceSize, "fvar instances");
        var instances = ImmutableArray.CreateBuilder<OpenTypeNamedInstance>(instanceCount);
        for (var index = 0; index < instanceCount; index++)
        {
            var offset = instancesOffset + index * instanceSize;
            var name = Name(names, UInt16(table, offset))
                ?? throw new InvalidDataException("OpenType fvar instance has no usable subfamily name.");
            var coordinates = ImmutableDictionary.CreateBuilder<uint, float>();
            for (var axisIndex = 0; axisIndex < axisCount; axisIndex++)
            {
                var axis = axes[axisIndex];
                var value = Fixed(table, offset + 4 + axisIndex * 4);
                if (value < axis.Minimum || value > axis.Maximum)
                {
                    throw new InvalidDataException("OpenType fvar instance coordinate is outside the axis range.");
                }
                coordinates.Add(axis.Tag, value);
            }
            string? postScriptName = null;
            if (instanceSize >= minimumInstanceSize + 2)
            {
                var nameId = UInt16(table, offset + minimumInstanceSize);
                if (nameId != ushort.MaxValue)
                {
                    postScriptName = Name(names, nameId)
                        ?? throw new InvalidDataException("OpenType fvar instance has no usable PostScript name.");
                }
            }
            instances.Add(new(index + 1, name, postScriptName, coordinates.ToImmutable()));
        }
        return metadata with { Axes = axes.MoveToImmutable(), NamedInstances = instances.MoveToImmutable() };
    }

    private static ReadOnlySpan<byte> NameBytes(ReadOnlySpan<byte> table, int storageOffset, int stringOffset, int length)
    {
        var offset = storageOffset + stringOffset;
        RequireRange(table, offset, length, "name string");
        return table.Slice(offset, length);
    }

    private static string DecodeUnicode(ReadOnlySpan<byte> bytes)
    {
        if ((bytes.Length & 1) != 0)
        {
            throw new InvalidDataException("OpenType Unicode name has an odd byte length.");
        }
        try
        {
            return unicode.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("OpenType name contains invalid UTF-16 data.", exception);
        }
    }

    private static string DecodeMacRoman(ReadOnlySpan<byte> bytes)
    {
        var characters = new char[bytes.Length];
        for (var index = 0; index < bytes.Length; index++)
        {
            var value = bytes[index];
            characters[index] = value < 128 ? (char)value : MAC_ROMAN_HIGH_CHARACTERS[value - 128];
        }
        return new(characters);
    }

    private static string? Name(ImmutableArray<OpenTypeNameRecord> names, ushort nameId)
    {
        return names.Where(name => name.NameId == nameId).OrderByDescending(name => name.Priority).Select(name => name.Value).FirstOrDefault();
    }

    private static ushort UInt16(ReadOnlySpan<byte> table, int offset)
    {
        return BinaryPrimitives.ReadUInt16BigEndian(table[offset..]);
    }

    private static float Fixed(ReadOnlySpan<byte> table, int offset)
    {
        return BinaryPrimitives.ReadInt32BigEndian(table[offset..]) / 65536f;
    }

    private static void ValidateTableLength(ReadOnlySpan<byte> table, string tableName)
    {
        if (table.Length > MAX_TABLE_BYTES)
        {
            throw new InvalidDataException($"OpenType {tableName} table exceeds {MAX_TABLE_BYTES} bytes.");
        }
    }

    private static void RequireRange(ReadOnlySpan<byte> table, int offset, long length, string field)
    {
        if (offset < 0 || length < 0 || offset > table.Length || length > table.Length - offset)
        {
            throw new InvalidDataException($"OpenType {field} exceeds its table bounds.");
        }
    }
}
