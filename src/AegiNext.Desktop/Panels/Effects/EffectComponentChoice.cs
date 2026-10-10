namespace AegiNext.Desktop.Panels.Effects;

internal sealed record EffectComponentChoice(int Mask, string Name)
{
    public override string ToString() => Name;
}
