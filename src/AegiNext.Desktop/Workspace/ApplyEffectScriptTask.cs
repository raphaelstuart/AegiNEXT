using AegiNext.Application;
using AegiNext.Application.Tasks;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed class ApplyEffectScriptTask(WorkbenchSession session, string source, IReadOnlyCollection<Guid> layerIds,
    ProjectDocument captured, long inputRevision, AnimationTrackTarget? targetContext = null) : ProjectWorkflowTask<bool>(session)
{
    public override string Name => "Tasks.ApplyEffectScript";
    protected override async Task<bool> ExecuteResultAsync(AegiTaskExecutionContext context)
    {
        var prepared = await Task.Run(() => ProjectEditingOperations.ApplyEffectScript(captured,
            layerIds, EffectScriptParser.Parse(source), targetContext: targetContext, cancellationToken: context.CancellationToken), context.CancellationToken);
        using var editLease = context.AcquireEditLease();
        context.EnterCommit(() => !Session.IsClosing && Session.TaskInputRevision == inputRevision &&
            !Session.HasProjectDrafts && ReferenceEquals(captured, Session.Editor.Snapshot));
        Session.Editor.Apply("Apply effect script", _ => prepared);
        Session.LogInfo("Effects", Localization.Get("Workbench.ApplyPreset"));
        return true;
    }
}
