namespace AegiNext.Application.Tasks;

/// <summary>Declares whether a task shares execution slots or forms an exclusive barrier.</summary>
public enum AegiTaskMode
{
    Parallel,
    Blocking,
}
