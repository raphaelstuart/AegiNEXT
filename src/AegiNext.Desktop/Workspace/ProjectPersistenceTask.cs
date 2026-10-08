using AegiNext.Application.Tasks;

namespace AegiNext.Desktop.Workspace;

internal sealed class ProjectPersistenceTask<TResult>(string scopeId, IReadOnlyCollection<AegiTaskResource> resources,
    string name, Func<Task<TResult>> execute) : AegiTask<TResult>
{
    public override string Name => name;
    public override string ScopeId => scopeId;
    public override IReadOnlyCollection<AegiTaskResource> Resources => resources;

    protected override Task<TResult> ExecuteResultAsync(AegiTaskExecutionContext context)
    {
        return execute();
    }
}
