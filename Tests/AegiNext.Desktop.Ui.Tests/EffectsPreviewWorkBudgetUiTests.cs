using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using Avalonia.Headless.XUnit;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class EffectsPreviewWorkBudgetUiTests
{
    [AvaloniaFact]
    public async Task UnchangedOperationOverlaysKeepTheCommittedSnapshotWithoutProjectSizedAllocations()
    {
        await using var context = new MainWindowTestContext();
        var source = ProjectEditingOperations.CreateSubtitleClips(context.Session.DocumentSnapshot,
            Enumerable.Range(0, 3000).Select(index => new SubtitleLine
            {
                Start = new MediaTime(index * 2), End = new MediaTime(index * 2 + 1), Text = $"Subtitle {index} 中文"
            }), context.Session.CurrentTrackId!.Value);
        context.Session.Editor.Reset(source);
        var effects = context.ViewModel.Effects;
        Assert.Same(source, effects.OverlayOperationDraft(source));

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 0; frame < 8; frame++)
        {
            Assert.Same(source, effects.OverlayOperationDraft(source));
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.True(allocated < 128 * 1024, $"Unchanged operation previews allocated {allocated} bytes in eight frames.");
    }
}
