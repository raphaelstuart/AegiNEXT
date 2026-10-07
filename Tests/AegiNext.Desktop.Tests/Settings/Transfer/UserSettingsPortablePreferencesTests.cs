using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Media;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Tests.Settings.Transfer;

/// <summary>完整设置包使用的偏好与布局内存接口、跨平台路径以及无副作用读取边界。</summary>
[Collection("Workspace session")]
public sealed class UserSettingsPortablePreferencesTests
{
    /// <summary>完整偏好往返保留个人快捷键、项目策略和设备校准，忽略派生的延迟属性。</summary>
    [Fact]
    public void CompletePreferencesRoundTripPreservesAllSavedValuesAndCalibration()
    {
        var expected = CreatePreferences();

        var bytes = WorkbenchPreferencesStore.Serialize(expected);
        var actual = WorkbenchPreferencesStore.Deserialize(bytes);

        Assert.Equal(expected, actual);
        Assert.Equal(expected.ShortcutBindings.ToArray(), actual.ShortcutBindings.ToArray());
        Assert.Equal(expected.AudioCalibrations.ToArray(), actual.AudioCalibrations.ToArray());
        using var document = JsonDocument.Parse(bytes);
        Assert.False(document.RootElement.GetProperty("AudioCalibrations")[0].TryGetProperty("Delay", out _));
    }

    /// <summary>偏好文档拒绝缺失、未知或重复字段及非法版本、枚举和嵌套校准数据。</summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("nested-missing")]
    [InlineData("nested-unknown")]
    [InlineData("nested-duplicate")]
    [InlineData("version")]
    [InlineData("enum")]
    [InlineData("null-projects")]
    [InlineData("calibration-missing")]
    [InlineData("calibration-invalid")]
    public void InvalidPreferencesDocumentsFailBeforeReturningDefaults(string corruption)
    {
        var json = JsonNode.Parse(WorkbenchPreferencesStore.Serialize(CreatePreferences()))!.AsObject();
        switch (corruption)
        {
            case "missing":
                json.Remove("Volume");
                break;
            case "unknown":
                json["Unexpected"] = true;
                break;
            case "nested-missing":
                json["Projects"]!.AsObject().Remove("AutoSaveEnabled");
                break;
            case "nested-unknown":
                json["Projects"]!.AsObject()["Unexpected"] = true;
                break;
            case "version":
                json["Version"] = 99;
                break;
            case "enum":
                json["Theme"] = 99;
                break;
            case "null-projects":
                json["Projects"] = null;
                break;
            case "calibration-missing":
                json["AudioCalibrations"]![0]!.AsObject().Remove("Backend");
                break;
            case "calibration-invalid":
                json["AudioCalibrations"]![0]!["SampleRate"] = 1;
                break;
        }
        var text = json.ToJsonString();
        text = corruption switch
        {
            "duplicate" => "{\"Version\":1," + text[1..],
            "nested-duplicate" => text.Replace("\"AutoSaveEnabled\":", "\"AutoSaveEnabled\":true,\"AutoSaveEnabled\":", StringComparison.Ordinal),
            _ => text
        };

        Assert.Throws<InvalidDataException>(() => WorkbenchPreferencesStore.Deserialize(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>无效 JSON 根和非法 UTF-8 在内存读取中明确失败，而不是回退为默认偏好。</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{broken")]
    public void InvalidPreferencesRootsAreRejected(string text)
    {
        Assert.Throws<InvalidDataException>(() => WorkbenchPreferencesStore.Deserialize(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>UTF-8 校验涵盖合法文档内的文本值，不允许替换无效字节后继续导入。</summary>
    [Fact]
    public void PreferencesRejectInvalidUtf8InsideAnOtherwiseValidTextValue()
    {
        var expected = CreatePreferences() with
        {
            AudioCalibrations = [new("UTF8MARKER", "backend", 48000, 2, 75)]
        };
        var bytes = WorkbenchPreferencesStore.Serialize(expected);
        var marker = Encoding.UTF8.GetBytes("UTF8MARKER");
        var index = bytes.AsSpan().IndexOf(marker);
        Assert.True(index >= 0);
        bytes[index] = 0xFF;

        Assert.Throws<InvalidDataException>(() => WorkbenchPreferencesStore.Deserialize(bytes));
    }

    /// <summary>偏好字节预算允许恰好 64 KiB 的有效文档，超出一字节即整体拒绝。</summary>
    [Fact]
    public void PreferencesEnforceExactMaximumDocumentSize()
    {
        var expected = CreatePreferences();
        var bytes = PadDocument(WorkbenchPreferencesStore.Serialize(expected), WorkbenchPreferencesStore.MAXIMUM_FILE_BYTES);

        Assert.Equal(expected, WorkbenchPreferencesStore.Deserialize(bytes));
        Assert.Throws<InvalidDataException>(() => WorkbenchPreferencesStore.Deserialize([.. bytes, (byte)' ']));
        Assert.Throws<InvalidDataException>(() => WorkbenchPreferencesStore.Serialize(expected with { Volume = float.NaN }));
    }

    /// <summary>便携模式保留 Windows drive、UNC 和 POSIX workspace 原文，普通读取仍使用目标平台验证。</summary>
    [Theory]
    [InlineData("C:\\Users\\source\\AegiNext\\Workspace")]
    [InlineData("\\\\source-server\\share\\Workspace")]
    [InlineData("/home/source/AegiNext/Workspace")]
    public void PortablePreferencesPreserveSourceWorkspaceWithoutCreatingDirectories(string workspace)
    {
        var expected = CreatePreferences() with { Projects = new() { WorkspaceRoot = workspace } };

        var bytes = WorkbenchPreferencesStore.Serialize(expected, allowForeignWorkspace: true);
        var actual = WorkbenchPreferencesStore.Deserialize(bytes, allowForeignWorkspace: true);

        Assert.Equal(expected, actual);
        Assert.Equal(workspace, actual.Projects.WorkspaceRoot);
        if (Path.IsPathFullyQualified(workspace))
        {
            Assert.Equal(expected, WorkbenchPreferencesStore.Deserialize(bytes));
        }
        else
        {
            Assert.Throws<InvalidDataException>(() => WorkbenchPreferencesStore.Deserialize(bytes));
            Assert.Throws<InvalidDataException>(() => WorkbenchPreferencesStore.Serialize(expected));
        }
    }

    /// <summary>便携模式不能把相对路径或 drive 相对路径当作外平台绝对 workspace。</summary>
    [Theory]
    [InlineData("relative/Workspace")]
    [InlineData("../Workspace")]
    [InlineData("C:Workspace")]
    [InlineData("")]
    public void PortablePreferencesRejectRelativeWorkspacePaths(string workspace)
    {
        var value = CreatePreferences() with { Projects = new() { WorkspaceRoot = workspace } };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);

        Assert.Throws<InvalidDataException>(() => WorkbenchPreferencesStore.Serialize(value, allowForeignWorkspace: true));
        Assert.Throws<InvalidDataException>(() => WorkbenchPreferencesStore.Deserialize(bytes, allowForeignWorkspace: true));
    }

    /// <summary>允许外平台路径不会绕过其余偏好与项目保护策略的完整验证。</summary>
    [Fact]
    public void ForeignWorkspaceAllowanceDoesNotBypassOtherPreferenceValidation()
    {
        var workspace = OperatingSystem.IsWindows() ? "/home/source/Workspace" : "C:\\Users\\source\\Workspace";
        var value = CreatePreferences() with
        {
            Projects = new() { WorkspaceRoot = workspace, MaximumBackupCount = 0 }
        };

        Assert.Throws<InvalidDataException>(() => WorkbenchPreferencesStore.Serialize(value, allowForeignWorkspace: true));
        Assert.Throws<InvalidDataException>(() => WorkbenchPreferencesStore.Deserialize(JsonSerializer.SerializeToUtf8Bytes(value), allowForeignWorkspace: true));
    }

    /// <summary>布局往返保留当前空间与独立个人预设；复制内置拓扑的个人记录仍是用户数据。</summary>
    [Fact]
    public void LayoutRoundTripPreservesCurrentTopologyAndPersonalCopyOfBuiltInLayout()
    {
        var expected = CreateLayout();

        var bytes = WorkspaceLayoutStore.Serialize(expected);
        var actual = WorkspaceLayoutStore.Deserialize(bytes);

        Assert.Equal(bytes, WorkspaceLayoutStore.Serialize(actual));
        Assert.Equal(expected.CurrentPresetId, actual.CurrentPresetId);
        Assert.Equal(WorkspaceLayoutStore.Fingerprint(expected.Current), WorkspaceLayoutStore.Fingerprint(actual.Current));
        var personal = Assert.Single(actual.Presets);
        Assert.False(personal.IsReadOnly);
        Assert.Equal("user-standard-copy", personal.Id);
        Assert.Equal(WorkspaceLayoutStore.Fingerprint(WorkspaceLayoutPresets.Standard), WorkspaceLayoutStore.Fingerprint(personal.Layout));
    }

    /// <summary>严格布局读取拒绝缺失、未知、重复字段、内置身份占用、只读个人项和非法拓扑。</summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("nested-missing")]
    [InlineData("nested-unknown")]
    [InlineData("version")]
    [InlineData("null-current")]
    [InlineData("null-presets")]
    [InlineData("reserved-id")]
    [InlineData("readonly")]
    [InlineData("unknown-selection")]
    [InlineData("unknown-focus")]
    [InlineData("incomplete-topology")]
    public void InvalidLayoutDocumentsAreRejected(string corruption)
    {
        var json = JsonNode.Parse(WorkspaceLayoutStore.Serialize(CreateLayout()))!.AsObject();
        switch (corruption)
        {
            case "missing":
                json.Remove("Current");
                break;
            case "unknown":
                json["Unexpected"] = true;
                break;
            case "nested-missing":
                json["Current"]!.AsObject().Remove("HiddenPanelIds");
                break;
            case "nested-unknown":
                json["Current"]!.AsObject()["Unexpected"] = true;
                break;
            case "version":
                json["Version"] = 99;
                break;
            case "null-current":
                json["Current"] = null;
                break;
            case "null-presets":
                json["Presets"] = null;
                break;
            case "reserved-id":
                json["Presets"]![0]!["Id"] = WorkspaceLayoutPresets.STANDARD;
                break;
            case "readonly":
                json["Presets"]![0]!["IsReadOnly"] = true;
                break;
            case "unknown-selection":
                json["CurrentPresetId"] = "user-missing";
                break;
            case "unknown-focus":
                json["Current"]!["FocusedPanelId"] = "unknown-panel";
                break;
            case "incomplete-topology":
                json["Current"]!["Main"] = JsonSerializer.SerializeToNode(new LayoutNodeSnapshot
                {
                    Kind = "panel", PanelId = WorkbenchPanelIds.PREVIEW
                });
                json["Current"]!["HiddenPanelIds"] = new JsonArray();
                break;
        }
        var text = json.ToJsonString();
        if (corruption == "duplicate")
        {
            text = "{\"Version\":" + WorkspaceLayoutSnapshot.CURRENT_VERSION + "," + text[1..];
        }

        Assert.Throws<InvalidDataException>(() => WorkspaceLayoutStore.Deserialize(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>布局文档严格检查 UTF-8 与 4 MiB 字节预算，恰好达到预算仍可读取。</summary>
    [Fact]
    public void LayoutEnforcesUtf8AndExactMaximumDocumentSize()
    {
        var expected = CreateLayout();
        var bytes = PadDocument(WorkspaceLayoutStore.Serialize(expected), WorkspaceLayoutStore.MAXIMUM_FILE_BYTES);

        Assert.Equal(WorkspaceLayoutStore.Serialize(expected), WorkspaceLayoutStore.Serialize(WorkspaceLayoutStore.Deserialize(bytes)));
        Assert.Throws<InvalidDataException>(() => WorkspaceLayoutStore.Deserialize([.. bytes, (byte)' ']));
        var invalidUtf8 = WorkspaceLayoutStore.Serialize(expected);
        var marker = Encoding.UTF8.GetBytes("My standard copy");
        var index = invalidUtf8.AsSpan().IndexOf(marker);
        Assert.True(index >= 0);
        invalidUtf8[index] = 0xFF;
        Assert.Throws<InvalidDataException>(() => WorkspaceLayoutStore.Deserialize(invalidUtf8));
    }

    /// <summary>缺失布局严格读取仅返回空个人库，不创建目录或诊断文件。</summary>
    [Fact]
    public void MissingLayoutStrictReadHasNoFilesystemSideEffects()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var missing = Path.Combine(directory.Path, "missing");
        var store = new WorkspaceLayoutStore(missing);

        var loaded = store.LoadStrict();

        Assert.Empty(loaded.Presets);
        Assert.Equal(WorkspaceLayoutPresets.STANDARD, loaded.CurrentPresetId);
        Assert.False(Directory.Exists(missing));
        Assert.Null(store.DiagnosticPath);
        Assert.Null(store.LoadError);
    }

    /// <summary>严格文件读取面对损坏内容明确失败并保留原文，不执行普通启动读取的诊断复制。</summary>
    [Fact]
    public void CorruptLayoutStrictReadDoesNotFallbackOrCreateDiagnostics()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var path = Path.Combine(directory.Path, "layouts.json");
        File.WriteAllText(path, "{broken");
        var store = new WorkspaceLayoutStore(directory.Path);

        Assert.Throws<InvalidDataException>(store.LoadStrict);

        Assert.Equal("{broken", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(directory.Path));
        Assert.Null(store.DiagnosticPath);
        Assert.Null(store.LoadError);
    }

    /// <summary>超预算布局文件在严格读取中拒绝，原文件不缩短也不生成恢复诊断。</summary>
    [Fact]
    public void OversizedLayoutStrictReadPreservesFileAndCreatesNoDiagnostics()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var path = Path.Combine(directory.Path, "layouts.json");
        using (var stream = File.Create(path))
        {
            stream.SetLength(WorkspaceLayoutStore.MAXIMUM_FILE_BYTES + 1L);
        }
        var store = new WorkspaceLayoutStore(directory.Path);

        Assert.Throws<InvalidDataException>(store.LoadStrict);

        Assert.Equal(WorkspaceLayoutStore.MAXIMUM_FILE_BYTES + 1L, new FileInfo(path).Length);
        Assert.Single(Directory.GetFiles(directory.Path));
        Assert.Null(store.DiagnosticPath);
        Assert.Null(store.LoadError);
    }

    private static WorkbenchPreferences CreatePreferences()
    {
        return new()
        {
            Language = "fr-FR",
            Theme = WorkbenchTheme.DARK,
            AccentColor = "#5273E8",
            Volume = 0.375f,
            WindowMenuOnMac = true,
            TimelineClassicTimingEnabled = true,
            TimelineSnapEnabled = false,
            TimelineStepEnabled = true,
            TimelineSpectrumVisible = false,
            TimelineWaveformVisible = false,
            ShortcutBindings = ShortcutDefaults.CreateBindings().Select(binding =>
                binding.Command == WorkbenchCommand.NEW_PROJECT ? binding with { Gesture = "F24" } : binding).ToImmutableArray(),
            Projects = new()
            {
                WorkspaceRoot = Path.Combine(Path.GetTempPath(), "AegiNext-portable-tests-workspace"),
                AutoSaveEnabled = false,
                AutoSaveIntervalMinutes = 11,
                BackupEnabled = true,
                BackupIntervalMinutes = 17,
                MaximumBackupCount = 29
            },
            AudioCalibrations = [new AudioDeviceCalibration("source-device", "source-backend", 48000, 2, 75)]
        };
    }

    private static WorkspaceLayoutFile CreateLayout()
    {
        var personal = new WorkspaceLayoutPreset("user-standard-copy", "My standard copy", false, WorkspaceLayoutPresets.Standard);
        return new()
        {
            Current = WorkspaceLayoutPresets.BuiltIn.Single(preset => preset.Id == WorkspaceLayoutPresets.EFFECTS).Layout,
            CurrentPresetId = personal.Id,
            Presets = [personal]
        };
    }

    private static byte[] PadDocument(byte[] bytes, int length)
    {
        Assert.True(bytes.Length < length);
        var result = new byte[length];
        result.AsSpan().Fill((byte)' ');
        bytes.CopyTo(result, 0);
        return result;
    }
}
