using AegiNext.Application.Presets;
using AegiNext.Application.Tasks;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.Workspace;

internal sealed class ExportEffectScriptTask(WorkbenchSession session, string path, string source) : AegiTask
{
    public override string Name => "Tasks.ExportEffectScript";
    public override string ScopeId => session.TaskScope;
    public override string ScopeDisplayName => session.ProjectDisplayName;
    public override IReadOnlyCollection<AegiTaskResource> Resources => [AegiTaskResource.DeferredStoragePath(path)];
    protected override async Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        if (AegiTaskResource.StoragePath(path) == session.ApplicationContext.GetCanonicalLibraryResource(PersonalLibraryKind.EFFECT))
        {
            throw new InvalidDataException("导出位置不能是正在使用的效果库文件。");
        }
        var temporary = Path.GetFullPath(path) + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await EffectScriptPresetStore.WriteScriptAsync(source, temporary, context.CancellationToken);
            context.EnterCommit();
            File.Move(temporary, path, true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
