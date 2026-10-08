using AegiNext.Application.Tasks;

namespace AegiNext.Desktop.Startup;

internal sealed class PersonalLibraryTask(DesktopApplicationContext owner, PersonalLibraryKind kind,
    Func<Task> operation, bool propagateFailure) : AegiTask
{
    public override string Name => kind switch
    {
        PersonalLibraryKind.STYLE => "Tasks.StyleLibrary",
        PersonalLibraryKind.EFFECT => "Tasks.EffectLibrary",
        _ => "Tasks.ExportPresetLibrary"
    };
    public override bool CanCancel => false;
    public override IReadOnlyCollection<AegiTaskResource> Resources => [owner.GetLibraryResource(kind)];

    protected override Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        return owner.ExecuteLibraryOperationAsync(operation, kind, propagateFailure);
    }
}
