using System.Collections.Immutable;
using AegiNext.Application;
using AegiNext.Application.Tasks;
using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Workspace;

internal sealed class ImportSubtitleFontTask(WorkbenchSession session, string path, Guid subtitleId,
    ProjectDocument captured, long inputRevision, string directory) : ProjectWorkflowTask<bool>(session)
{
    public override string Name => "Tasks.ImportFont";
    protected override async Task<bool> ExecuteResultAsync(AegiTaskExecutionContext context)
    {
        var asset = await ProjectResources.ImportAsync(path, ProjectAssetKind.FONT, directory, context.CancellationToken);
        using var editLease = context.AcquireEditLease();
        context.EnterCommit(() => !Session.IsClosing && Session.ProjectDirectory == directory &&
            Session.TaskInputRevision == inputRevision && !Session.HasProjectDrafts && ReferenceEquals(captured, Session.Editor.Snapshot));
        Session.Editor.Apply("Import font", document => document with
        {
            Assets = document.Assets.Add(asset),
            Subtitles = document.Subtitles.Select(line => line.Id == subtitleId
                ? line with { Style = line.Style with { FontAssetId = asset.Id } } : line).ToImmutableArray()
        });
        return true;
    }
}
