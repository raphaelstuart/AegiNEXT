using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using AegiNext.Desktop.Settings.Transfer;

namespace AegiNext.Desktop.Tests.Settings.Transfer;

/// <summary>验证版本化用户设置包的完整往返和严格、有界 ZIP 读取。</summary>
[Collection("Workspace session")]
public sealed class UserSettingsBundleStoreTests
{
    private static readonly string[] archiveFileNames =
    [
        "effect-scripts.json", "export-presets.aegiexports", "layouts.json", "manifest.json", "preferences.json", "subtitle-styles.aegistyles"
    ];

    /// <summary>五种设置通过固定条目完整往返，所有个人参数保持原值。</summary>
    [Fact]
    public async Task CompleteBundleRoundTripsThroughMemoryAndAtomicFile()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var bundle = UserSettingsTransferTestData.CreateBundle();
        var bytes = UserSettingsBundleStore.Serialize(bundle);
        var entries = UserSettingsTransferTestData.ReadArchive(bytes);

        Assert.Equal(6, entries.Count);
        Assert.Equal(archiveFileNames,
            entries.Keys.Order(StringComparer.Ordinal).ToArray());
        UserSettingsTransferTestData.AssertBundleEqual(bundle, UserSettingsBundleStore.Deserialize(bytes));
        var path = Path.Combine(directory.Path, "settings.aegisettings");
        await UserSettingsBundleStore.SaveAsync(bundle, path);
        UserSettingsTransferTestData.AssertBundleEqual(bundle, await UserSettingsBundleStore.LoadAsync(path));
        Assert.Single(Directory.GetFiles(directory.Path));
    }

    /// <summary>没有个人模板时仍导出完整空集合，不将内置内容加入包。</summary>
    [Fact]
    public void StyleMarginsRoundTripInTheBundleAndLegacyScalarStylesMigrate()
    {
        var original = UserSettingsTransferTestData.CreateBundle();
        var preset = original.Styles.Presets[0];
        var independent = original with
        {
            Styles = original.Styles with
            {
                Presets = [preset with { Style = preset.Style with { Margins = new(13.125, 41.25, 27.5) } }]
            }
        };
        UserSettingsTransferTestData.AssertBundleEqual(independent,
            UserSettingsBundleStore.Deserialize(UserSettingsBundleStore.Serialize(independent)));

        var entries = UserSettingsTransferTestData.ReadArchive(UserSettingsBundleStore.Serialize(original));
        var library = JsonNode.Parse(entries["subtitle-styles.aegistyles"])!.AsObject();
        library["version"] = 4;
        var style = library["presets"]![0]!["style"]!.AsObject();
        style.Remove("margins");
        style["margin"] = 40;
        entries["subtitle-styles.aegistyles"] = Encoding.UTF8.GetBytes(library.ToJsonString());

        UserSettingsTransferTestData.AssertBundleEqual(original,
            UserSettingsBundleStore.Deserialize(UserSettingsTransferTestData.WriteArchive(entries)));
    }

    [Fact]
    public void DefaultBundleContainsEmptyPersonalLibraries()
    {
        var bundle = UserSettingsBundleStore.Deserialize(UserSettingsBundleStore.Serialize(new()));

        Assert.Empty(bundle.Styles.Presets);
        Assert.Empty(bundle.Effects.Presets);
        Assert.Empty(bundle.ExportPresets.Presets);
        Assert.Empty(bundle.Layouts.Presets);
    }

    /// <summary>未知、重复、缺失或非整数版本清单均按无效包拒绝。</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"version\":2}")]
    [InlineData("{\"version\":\"1\"}")]
    [InlineData("{\"version\":true}")]
    [InlineData("{\"version\":1,\"version\":1}")]
    [InlineData("{\"version\":1,\"other\":0}")]
    public void InvalidManifestIsRejected(string manifest)
    {
        var entries = UserSettingsTransferTestData.ReadArchive(UserSettingsBundleStore.Serialize(new()));
        entries["manifest.json"] = Encoding.UTF8.GetBytes(manifest);

        Assert.Throws<InvalidDataException>(() => UserSettingsBundleStore.Deserialize(UserSettingsTransferTestData.WriteArchive(entries)));
    }

    /// <summary>非法 UTF-8 清单不会泄漏解码异常类型。</summary>
    [Fact]
    public void InvalidUtf8ManifestReturnsControlledFormatError()
    {
        var entries = UserSettingsTransferTestData.ReadArchive(UserSettingsBundleStore.Serialize(new()));
        entries["manifest.json"] = [0xFF];

        Assert.Throws<InvalidDataException>(() => UserSettingsBundleStore.Deserialize(UserSettingsTransferTestData.WriteArchive(entries)));
    }

    /// <summary>条目名称必须完全匹配，任何路径或未知大小写条目都无法作为配置读取。</summary>
    [Theory]
    [InlineData("../preferences.json")]
    [InlineData("preferences.json/")]
    [InlineData("folder/preferences.json")]
    [InlineData("PREFERENCES.JSON")]
    [InlineData("C:\\preferences.json")]
    public void UnknownAndPathEntriesAreRejected(string name)
    {
        var entries = UserSettingsTransferTestData.ReadArchive(UserSettingsBundleStore.Serialize(new()));
        var preferences = entries["preferences.json"];
        entries.Remove("preferences.json");
        entries[name] = preferences;

        Assert.Throws<InvalidDataException>(() => UserSettingsBundleStore.Deserialize(UserSettingsTransferTestData.WriteArchive(entries)));
    }

    /// <summary>重复或缺失配置条目不能构成完整设置包。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DuplicateAndMissingEntriesAreRejected(bool duplicate)
    {
        var entries = UserSettingsTransferTestData.ReadArchive(UserSettingsBundleStore.Serialize(new()));
        var altered = duplicate ? entries.Concat([new("preferences.json", entries["preferences.json"])]) :
            entries.Where(value => value.Key != "preferences.json");

        Assert.Throws<InvalidDataException>(() => UserSettingsBundleStore.Deserialize(UserSettingsTransferTestData.WriteArchive(altered)));
    }

    /// <summary>任一组成文档不完整都拒绝整包，不返回部分设置。</summary>
    [Theory]
    [InlineData("preferences.json")]
    [InlineData("subtitle-styles.aegistyles")]
    [InlineData("effect-scripts.json")]
    [InlineData("export-presets.aegiexports")]
    [InlineData("layouts.json")]
    public void IncompleteComponentRejectsEntireBundle(string name)
    {
        var entries = UserSettingsTransferTestData.ReadArchive(UserSettingsBundleStore.Serialize(new()));
        entries[name] = "{}"u8.ToArray();

        Assert.Throws<InvalidDataException>(() => UserSettingsBundleStore.Deserialize(UserSettingsTransferTestData.WriteArchive(entries)));
    }

    /// <summary>高压缩条目仍遵守解压后容量限制，即使目录谎报较小的长度。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExpandedEntryBudgetIsEnforcedBeyondDeclaredLength(bool forgedLength)
    {
        var entries = UserSettingsTransferTestData.ReadArchive(UserSettingsBundleStore.Serialize(new()));
        entries["preferences.json"] = new byte[65537];
        var bytes = UserSettingsTransferTestData.WriteArchive(entries);
        if (forgedLength)
        {
            RewriteDeclaredLength(bytes, "preferences.json", 1);
        }

        Assert.Throws<InvalidDataException>(() => UserSettingsBundleStore.Deserialize(bytes));
    }

    /// <summary>超大包在流读取前拒绝；已取消保存保持已有文件字节。</summary>
    [Fact]
    public async Task FileBudgetAndCancellationPreserveExistingDestination()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var large = Path.Combine(directory.Path, "large.aegisettings");
        using (var stream = File.Create(large))
        {
            stream.SetLength((long)UserSettingsBundleStore.MAXIMUM_FILE_BYTES + 1);
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => UserSettingsBundleStore.LoadAsync(large));
        var path = Path.Combine(directory.Path, "unchanged.aegisettings");
        var original = "existing bytes"u8.ToArray();
        await File.WriteAllBytesAsync(path, original);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UserSettingsBundleStore.SaveAsync(new(), path, cancellation.Token));
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    private static void RewriteDeclaredLength(byte[] bytes, string entryName, uint length)
    {
        for (var index = 0; index + 46 < bytes.Length; index++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index, 4)) != 0x02014B50)
            {
                continue;
            }
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(index + 28, 2));
            if (index + 46 + nameLength <= bytes.Length &&
                Encoding.UTF8.GetString(bytes, index + 46, nameLength) == entryName)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index + 24, 4), length);
                return;
            }
        }
        throw new InvalidOperationException("Test archive entry was not found.");
    }
}
