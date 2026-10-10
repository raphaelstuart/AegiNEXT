using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Panels.Effects;

internal sealed record EffectStateChoice(SubtitleAnimationState State, string Name)
{
    public override string ToString() => Name;
}
