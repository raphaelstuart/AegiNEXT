using AegiNext.Application.Presets;
using AegiNext.Core.Effects;
using AegiNext.Desktop.Localization;

namespace AegiNext.Desktop.Workspace;

internal sealed class EffectScriptLibraryCoordinator(WorkbenchSession session, IWorkbenchDialogService dialogs)
{
    private int queuedOperations;
    private EffectScriptChoice[] choices = [];
    public Task Completion { get; private set; } = Task.CompletedTask;
    internal bool IsBusy => queuedOperations > 0;

    internal void Initialize() => Queue(async () =>
    {
        await session.EffectScriptLibrary.LoadAsync();
        RefreshChoices();
    });

    internal void Queue(Func<Task> action)
    {
        queuedOperations++;
        Completion = RunAsync(Completion, action);
        session.NotifyEffectLibraryChanged();
    }

    private async Task RunAsync(Task previous, Func<Task> action)
    {
        try
        {
            await previous;
            if (!session.IsClosing)
            {
                await session.RunCommandAsync(action);
            }
        }
        finally
        {
            queuedOperations--;
            session.NotifyEffectLibraryChanged();
        }
    }

    internal void RefreshChoices()
    {
        var vm = session.ViewModel.Effects;
        var selectedId = vm.Preset >= 0 && vm.Preset < choices.Length ? choices[vm.Preset].Id : null;
        choices = BuiltinEffectScripts.Templates.Select(template => new EffectScriptChoice(template.Script.Id,
            BuiltinName(template.Script.Id), template.Source))
            .Concat(session.EffectScriptLibrary.Snapshot.Presets.Select(preset => new EffectScriptChoice(preset.Id.ToString("D"), preset.Name, preset.Source)))
            .ToArray();
        var names = choices.Select(choice => choice.Name).ToArray();
        if (!vm.Presets.SequenceEqual(names))
        {
            vm.Presets = names;
        }
        var index = Array.FindIndex(choices, choice => choice.Id == selectedId);
        vm.Preset = choices.Length == 0 ? -1 : Math.Max(0, index);
    }

    internal void ApplySelected()
    {
        var index = session.ViewModel.Effects.Preset;
        if (index >= 0 && index < choices.Length)
        {
            Apply(choices[index].Source);
        }
    }

    internal void Apply(string source)
    {
        if (session.SelectedLayer is not { SubtitleId: not null } layer)
        {
            return;
        }
        session.ViewModel.CancelGestures();
        session.ClearKeyframeSelection();
        session.Editor.ApplyEffectScript(layer.Id, EffectScriptParser.Parse(source));
        session.LogInfo("Effects", WorkbenchText.Get("ApplyPreset"));
    }

    internal async Task UpsertAsync(EffectScriptPreset preset)
    {
        await session.EffectScriptLibrary.UpsertAsync(preset);
        RefreshChoices();
        session.NotifyEffectLibraryChanged();
        session.LogInfo("Effects", WorkbenchText.Get("SavePreset"), preset.Name);
    }

    internal async Task DeleteAsync(Guid id)
    {
        await session.EffectScriptLibrary.RemoveAsync(id);
        RefreshChoices();
        session.NotifyEffectLibraryChanged();
    }

    internal async Task ImportAsync()
    {
        var path = await dialogs.OpenFileAsync("ImportEffectScripts", "EffectScriptFiles", ["*.aegifx"]);
        if (path is null)
        {
            return;
        }
        await session.EffectScriptLibrary.ImportAsync(path);
        RefreshChoices();
        session.NotifyEffectLibraryChanged();
    }

    internal async Task ExportAsync(EffectScriptPreset preset)
    {
        var script = EffectScriptParser.Parse(preset.Source);
        var path = await dialogs.SaveFileAsync("ExportEffectScripts", "EffectScriptFiles", ["*.aegifx"], ".aegifx", script.Id + ".aegifx");
        if (path is not null)
        {
            await EffectScriptPresetStore.WriteScriptAsync(preset.Source, path);
        }
    }

    private static string BuiltinName(string id) => WorkbenchText.Get(id switch
    {
        "fade-in-out" => "Fade", "fade-in" => "FadeIn", "fade-out" => "FadeOut",
        "pop-in" => "Pop", "pop-out" => "PopOut", "slide-in" => "Slide", "slide-out" => "SlideOut",
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    });
}
