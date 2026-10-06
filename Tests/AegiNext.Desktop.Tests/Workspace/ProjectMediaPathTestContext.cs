using System.Collections.Concurrent;
using System.Globalization;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

internal sealed class ProjectMediaPathTestContext : IAsyncDisposable
{
    private readonly TemporaryWorkbenchDirectory directory = new();
    private readonly CultureInfo previousCulture = CultureInfo.CurrentUICulture;
    private readonly CultureInfo? previousDefaultCulture = CultureInfo.DefaultThreadCurrentUICulture;
    private readonly ConcurrentQueue<PreviewTestSource> sources = new();
    private readonly ConcurrentQueue<PreviewTestConverter> converters = new();

    internal ProjectMediaPathTestContext()
    {
        Session = new(Dialogs, update => new VideoPreviewController(ProbeAsync, (_, _) => new(_ =>
        {
            var source = new PreviewTestSource(7, 0, 100, 200);
            sources.Enqueue(source);
            return source;
        }), () =>
        {
            var converter = new PreviewTestConverter();
            converters.Enqueue(converter);
            return converter;
        }, DispatchAsync, update), DispatchAsync,
            preferencesStore: new WorkbenchPreferencesStore(directory.Path));
    }

    internal WorkbenchSession Session { get; }
    internal WorkspaceDialogStub Dialogs { get; } = new();
    internal ConcurrentQueue<string> ProbePaths { get; } = new();
    internal ConcurrentDictionary<string, Exception> ProbeFailures { get; } = new();
    internal Func<Action, CancellationToken, Task>? DispatchOverride { get; set; }

    internal void ChangeDocument(Action change)
    {
        Session.SetProjectBusy(true);
        try
        {
            change();
        }
        finally
        {
            Session.SetProjectBusy(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Session.DisposeAsync();
            Assert.All(sources, source => Assert.Equal(1, source.DisposeCount));
            Assert.All(converters, converter => Assert.Equal(1, converter.DisposeCount));
            Assert.All(sources.SelectMany(source => source.IssuedFrames), frame => Assert.Equal(1, frame.DisposeCount));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
            CultureInfo.DefaultThreadCurrentUICulture = previousDefaultCulture;
            directory.Dispose();
        }
    }

    private Task<VideoPreviewMedia> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProbePaths.Enqueue(path);
        if (ProbeFailures.TryGetValue(path, out var error))
        {
            throw error;
        }
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Media fixture was not found.", path);
        }

        return Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(1), VideoWidth: 1, VideoHeight: 1));
    }

    private Task DispatchAsync(Action action, CancellationToken cancellationToken)
    {
        if (DispatchOverride is { } customDispatch)
        {
            return customDispatch(action, cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
