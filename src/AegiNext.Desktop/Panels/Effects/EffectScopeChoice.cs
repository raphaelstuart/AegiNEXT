namespace AegiNext.Desktop.Panels.Effects;

internal sealed record EffectScopeChoice(Guid? Id, string Name)
{
    public override string ToString() => Name;
}
