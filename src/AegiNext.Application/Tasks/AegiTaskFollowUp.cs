namespace AegiNext.Application.Tasks;

internal sealed record AegiTaskFollowUp(AegiTask Task, Action<AegiTaskHandle?>? Submitted);
