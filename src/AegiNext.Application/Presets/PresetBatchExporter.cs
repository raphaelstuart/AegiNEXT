using System.Text;
using AegiNext.Core.Effects;
using AegiNext.Core.Presets;

namespace AegiNext.Application.Presets;

/// <summary>将显式选中的预设逐项暂存和发布，禁止覆盖既有目标；失败时清理本次输出，不构成多文件原子事务。</summary>
public static class PresetBatchExporter
{
    private const int MAXIMUM_STEM_BYTES = 180;

    /// <summary>每个所选样式输出一份包含该样式和内嵌字体的独立 .aegistyles 文件。</summary>
    public static async Task ExportStylesAsync(IEnumerable<SubtitleStylePreset> presets, string directory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(presets);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        cancellationToken.ThrowIfCancellationRequested();
        var selection = presets.ToArray();
        if (selection.Length == 0)
        {
            return;
        }

        var identities = new HashSet<Guid>();
        var entries = new List<(Guid Id, string Stem, Func<string, CancellationToken, Task> Write)>();
        foreach (var preset in selection)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SubtitleStylePresetValidator.Validate(preset);
            if (!identities.Add(preset.Id))
            {
                throw new InvalidDataException("待导出的样式预设标识重复。");
            }

            entries.Add((preset.Id, SafeStem(preset.Name), (path, token) =>
                SubtitleStylePresetStore.SaveAsync(new() { Presets = [preset] }, path, token)));
        }

        await ExportAsync(entries, directory, ".aegistyles", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>每个所选内置或个人脚本以脚本标识命名，输出一份保持原文的独立 .aegifx 文件。</summary>
    public static async Task ExportEffectsAsync(IEnumerable<EffectScriptPreset> presets, string directory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(presets);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        cancellationToken.ThrowIfCancellationRequested();
        var selection = presets.ToArray();
        if (selection.Length == 0)
        {
            return;
        }

        var identities = new HashSet<Guid>();
        var entries = new List<(Guid Id, string Stem, Func<string, CancellationToken, Task> Write)>();
        foreach (var preset in selection)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(preset);
            var script = EffectScriptParser.Parse(preset.Source);
            if (preset.Id == Guid.Empty || !identities.Add(preset.Id))
            {
                throw new InvalidDataException("待导出的特效模板标识无效或重复。");
            }

            entries.Add((preset.Id, SafeStem(script.Id), (path, token) =>
                EffectScriptPresetStore.WriteScriptAsync(preset.Source, path, token)));
        }

        await ExportAsync(entries, directory, ".aegifx", cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExportAsync(List<(Guid Id, string Stem, Func<string, CancellationToken, Task> Write)> entries,
        string directory, string extension, CancellationToken cancellationToken)
    {
        var destination = Path.GetFullPath(directory);
        var filenames = ResolveFilenames(entries.Select(entry => (entry.Id, entry.Stem)).ToArray(), extension);
        var paths = filenames.Select(filename => Path.Combine(destination, filename)).ToArray();
        foreach (var path in paths)
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                throw new IOException($"导出目标已存在，未覆盖任何文件：{path}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destination);
        var stagingDirectory = Path.Combine(destination, $".aeginext-export-{Guid.NewGuid():N}");
        var published = new List<string>();
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            for (var index = 0; index < entries.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await entries[index].Write(Path.Combine(stagingDirectory, filenames[index]), cancellationToken).ConfigureAwait(false);
            }

            for (var index = 0; index < entries.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(Path.Combine(stagingDirectory, filenames[index]), paths[index]);
                published.Add(paths[index]);
            }

            Directory.Delete(stagingDirectory);
        }
        catch (Exception failure)
        {
            var cleanupErrors = Cleanup(published, stagingDirectory);
            if (cleanupErrors.Count != 0)
            {
                throw new AggregateException("批量导出失败，部分本次输出未能清理。", [failure, .. cleanupErrors]);
            }

            throw;
        }
    }

    private static string[] ResolveFilenames((Guid Id, string Stem)[] entries, string extension)
    {
        var suffixed = new HashSet<Guid>();
        while (true)
        {
            var candidates = entries.Select(entry => entry.Stem + (suffixed.Contains(entry.Id) ? $"-{entry.Id:N}" : string.Empty)).ToArray();
            var conflicts = candidates.Select((candidate, index) => (candidate, entries[index].Id))
                .GroupBy(item => item.candidate, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1).ToArray();
            if (conflicts.Length == 0)
            {
                return candidates.Select(candidate => candidate + extension).ToArray();
            }

            foreach (var conflict in conflicts)
            {
                foreach (var entry in conflict)
                {
                    suffixed.Add(entry.Id);
                }
            }
        }
    }

    private static string SafeStem(string name)
    {
        var cleaned = new string(name.Select(character => char.IsControl(character) || "/\\:<>\"|?*".Contains(character, StringComparison.Ordinal)
            ? '_' : character).ToArray()).Normalize(NormalizationForm.FormC);
        var builder = new StringBuilder();
        var bytes = 0;
        foreach (var rune in cleaned.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > MAXIMUM_STEM_BYTES)
            {
                break;
            }

            builder.Append(rune.ToString());
            bytes += rune.Utf8SequenceLength;
        }

        var stem = builder.ToString().TrimEnd(' ', '.');
        if (stem.Length == 0)
        {
            stem = "preset";
        }

        var deviceStem = stem.Split('.')[0];
        if (deviceStem.Equals("CON", StringComparison.OrdinalIgnoreCase) || deviceStem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            deviceStem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || deviceStem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            deviceStem.Length == 4 && deviceStem[3] is >= '1' and <= '9' &&
            (deviceStem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || deviceStem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)))
        {
            stem = "_" + stem;
        }

        return stem;
    }

    private static List<Exception> Cleanup(List<string> published, string stagingDirectory)
    {
        var errors = new List<Exception>();
        foreach (var path in published.AsEnumerable().Reverse())
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                errors.Add(error);
            }
        }

        try
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, true);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            errors.Add(error);
        }

        return errors;
    }
}
