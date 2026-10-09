using AegiNext.Desktop.I18n;
using AegiNext.Application.Tasks;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace AegiNext.Desktop.Layouts;

internal sealed class WorkbenchLayoutController : IDisposable
{
    private readonly Window owner;
    private readonly Func<bool> commitDrafts;
    private readonly Action cancelGestures;
    private readonly Action<Window> registerWindow;
    private readonly Dictionary<string, WorkbenchDockPanel> panels;
    private readonly WorkbenchDockFactory factory;
    private readonly WorkbenchDockSnapshotCodec codec;
    private readonly WorkspaceLayoutStore store;
    private readonly SemaphoreSlim persistenceGate = new(1, 1);
    private readonly DockControl dockHost;
    private readonly DispatcherTimer persistenceTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly List<INotifyPropertyChanged> watchedProperties = [];
    private readonly List<INotifyCollectionChanged> watchedCollections = [];
    private readonly List<WorkbenchFloatingHostWindow> floatingHosts = [];
    private readonly List<Window> auxiliaryWindows = [];
    private readonly Dictionary<IDockWindow, double> restoredWindowScales = [];
    private readonly List<WorkspaceLayoutPreset> userPresets;
    private readonly Dictionary<string, int> unreadCounts = new(StringComparer.Ordinal);
    private IRootDock root = null!;
    private bool applying;
    private bool disposed;
    private string lastFingerprint = string.Empty;

    internal WorkbenchLayoutController(Window owner, IReadOnlyDictionary<string, Control> panelViews,
        string personalDirectory, Func<bool> commitDrafts, Action cancelGestures, Action<Window> registerWindow,
        AegiTaskService? tasks = null, WorkspaceLayoutFile? initialLayout = null)
    {
        if (!panelViews.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(WorkbenchPanelIds.All))
        {
            throw new ArgumentException("Every stable workspace panel view is required exactly once.", nameof(panelViews));
        }

        this.owner = owner;
        this.commitDrafts = commitDrafts;
        this.cancelGestures = cancelGestures;
        this.registerWindow = registerWindow;
        panels = panelViews.ToDictionary(pair => pair.Key,
            pair => new WorkbenchDockPanel(pair.Key, pair.Value, Localization.Get("Layout." + (pair.Key))), StringComparer.Ordinal);
        factory = new(CreateFloatingHost, cancelGestures, ScheduleCapture, commitDrafts);
        codec = new(factory, panels);
        store = new(personalDirectory, tasks);
        var file = initialLayout ?? store.Load();
        userPresets = file.Presets.ToList();
        CurrentPresetId = file.CurrentPresetId;
        dockHost = new() { Factory = factory, InitializeFactory = false, InitializeLayout = false };
        WorkbenchDockTemplateCatalog.Install(dockHost);
        dockHost.AddHandler(InputElement.PointerPressedEvent, OnDockSpacePointerPressed, RoutingStrategies.Tunnel, true);
        persistenceTimer.Tick += OnPersistenceTimer;
        owner.Opened += OnOwnerOpened;
        ApplySnapshot(file.Current);
        LastError = store.LoadError is null ? null : Localization.Get("Layout.Corrupt");
        DiagnosticPath = store.DiagnosticPath;
        Localization.LanguageChanged += OnLanguageChanged;
    }

    public event EventHandler? Changed;
    public event EventHandler? Error;
    public event Action<Window, string>? FloatingWindowTitleChanged;

    public Control Host => dockHost;
    public string CurrentPresetId { get; private set; }
    public bool IsModified { get; private set; }
    public string? LastError { get; private set; }
    public string? DiagnosticPath { get; }
    public IReadOnlyList<WorkspaceLayoutPreset> Presets => WorkspaceLayoutPresets.BuiltIn.Concat(userPresets).ToArray();
    public IReadOnlyList<Window> FloatingWindows => floatingHosts.Where(host => host.IsVisible).Cast<Window>().ToArray();

    internal IReadOnlyDictionary<string, WorkbenchDockPanel> PanelAdapters => panels;
    internal IRootDock Root => root;

    internal static string GetPresetName(WorkspaceLayoutPreset preset)
    {
        return preset.IsReadOnly ? Localization.Get("Layout." + (preset.Id)) : preset.Name;
    }

    internal bool IsVisible(string panelId)
    {
        var panel = GetPanel(panelId);
        return EnumerateRoots().Any(candidate => WorkbenchDockSnapshotCodec.Enumerate(candidate).Contains(panel));
    }

    internal void Show(string panelId)
    {
        EnsureUsable();
        cancelGestures();
        var panel = GetPanel(panelId);
        if (!IsVisible(panelId))
        {
            var originalOwner = panel.OriginalOwner as IToolDock;
            var hiddenRoot = EnumerateRoots().FirstOrDefault(candidate => candidate.HiddenDockables?.Contains(panel) == true);
            var canRestoreOwner = originalOwner is not null && EnumerateRoots()
                .Any(candidate => WorkbenchDockSnapshotCodec.Enumerate(candidate).Contains(originalOwner));
            if (canRestoreOwner && hiddenRoot is not null)
            {
                factory.RestoreDockable(panel);
            }
            else
            {
                foreach (var candidate in EnumerateRoots())
                {
                    candidate.HiddenDockables?.Remove(panel);
                }
                var tabs = FindReopenDock();
                panel.OriginalOwner = null;
                factory.AddDockable(tabs, panel);
            }
        }
        Activate(panelId);
        ScheduleCapture();
    }

    internal void Hide(string panelId)
    {
        EnsureUsable();
        if (panelId == WorkbenchPanelIds.SUBTITLE_DETAILS && !commitDrafts())
        {
            SetError(Localization.Get("Layout.InvalidDraft"));
            return;
        }
        cancelGestures();
        var panel = GetPanel(panelId);
        if (IsVisible(panelId))
        {
            factory.HideDockable(panel);
            foreach (var host in floatingHosts.ToArray())
            {
                if (host.Window?.Layout is { } floatingRoot
                    && !WorkbenchDockSnapshotCodec.Enumerate(floatingRoot).OfType<WorkbenchDockPanel>().Any())
                {
                    host.Close();
                }
            }
            ScheduleCapture();
        }
    }

    internal void Activate(string panelId)
    {
        EnsureUsable();
        var panel = GetPanel(panelId);
        if (!IsVisible(panelId))
        {
            Show(panelId);
            return;
        }
        factory.SetActiveDockable(panel);
        if (panel.Owner is IDock dock)
        {
            factory.SetFocusedDockable(dock, panel);
        }
        var floating = floatingHosts.FirstOrDefault(host => host.Window?.Layout is { } floatingRoot
            && WorkbenchDockSnapshotCodec.Enumerate(floatingRoot).Contains(panel));
        floating?.Activate();
        ScheduleCapture();
    }

    internal void Float(string panelId)
    {
        EnsureUsable();
        cancelGestures();
        Show(panelId);
        factory.FloatDockable(GetPanel(panelId));
        ScheduleCapture();
    }

    internal Task<bool> ApplyPresetAsync(string presetId) => RunLayoutOperationAsync(() => ApplyPresetCoreAsync(presetId));

    private async Task<bool> ApplyPresetCoreAsync(string presetId)
    {
        EnsureUsable();
        var preset = Presets.Single(candidate => candidate.Id == presetId);
        cancelGestures();
        if (!commitDrafts())
        {
            SetError(Localization.Get("Layout.InvalidDraft"));
            return false;
        }

        CurrentPresetId = preset.Id;
        ApplySnapshot(preset.Layout);
        LastError = null;
        Changed?.Invoke(this, EventArgs.Empty);
        await PersistCurrentCoreAsync();
        return true;
    }

    internal Task<bool> SaveAsync() => RunLayoutOperationAsync(SaveCoreAsync);

    private async Task<bool> SaveCoreAsync()
    {
        EnsureUsable();
        var index = userPresets.FindIndex(preset => preset.Id == CurrentPresetId);
        if (index < 0)
        {
            SetError(Localization.Get("Layout.ReadOnly"));
            return false;
        }
        var layout = Capture();
        userPresets[index] = userPresets[index] with { Layout = layout };
        IsModified = false;
        Changed?.Invoke(this, EventArgs.Empty);
        await PersistCurrentCoreAsync();
        return true;
    }

    internal Task<string?> SaveAsAsync(string name) => RunLayoutOperationAsync(() => SaveAsCoreAsync(name));

    private async Task<string?> SaveAsCoreAsync(string name)
    {
        EnsureUsable();
        name = name.Trim();
        if (!IsValidName(name, null))
        {
            SetError(Localization.Get("Layout.InvalidName"));
            return null;
        }
        var id = "user-" + Guid.NewGuid().ToString("N");
        userPresets.Add(new(id, name, false, Capture()));
        CurrentPresetId = id;
        IsModified = false;
        LastError = null;
        Changed?.Invoke(this, EventArgs.Empty);
        await PersistCurrentCoreAsync();
        return id;
    }

    internal Task<bool> RenameAsync(string presetId, string name) => RunLayoutOperationAsync(() => RenameCoreAsync(presetId, name));

    private async Task<bool> RenameCoreAsync(string presetId, string name)
    {
        EnsureUsable();
        name = name.Trim();
        var index = userPresets.FindIndex(preset => preset.Id == presetId);
        if (index < 0)
        {
            SetError(Localization.Get("Layout.ReadOnly"));
            return false;
        }
        if (!IsValidName(name, presetId))
        {
            SetError(Localization.Get("Layout.InvalidName"));
            return false;
        }
        userPresets[index] = userPresets[index] with { Name = name };
        LastError = null;
        Changed?.Invoke(this, EventArgs.Empty);
        await PersistCurrentCoreAsync();
        return true;
    }

    internal Task<bool> DeleteAsync(string presetId)
    {
        return DeleteAsync([presetId]);
    }

    internal Task<bool> DeleteAsync(IEnumerable<string> presetIds)
    {
        EnsureUsable();
        ArgumentNullException.ThrowIfNull(presetIds);
        var ids = presetIds.ToHashSet(StringComparer.Ordinal);
        return RunLayoutOperationAsync(() => DeleteCoreAsync(ids));
    }

    private async Task<bool> DeleteCoreAsync(HashSet<string> ids)
    {
        if (ids.Count == 0)
        {
            return true;
        }
        if (ids.Any(id => !userPresets.Any(preset => preset.Id == id)))
        {
            SetError(Localization.Get("Layout.ReadOnly"));
            return false;
        }
        var remaining = userPresets.Where(preset => !ids.Contains(preset.Id)).ToArray();
        var currentId = ids.Contains(CurrentPresetId) ? WorkspaceLayoutPresets.STANDARD : CurrentPresetId;
        persistenceTimer.Stop();
        await store.SaveAsync(new()
        {
            Current = Capture(), CurrentPresetId = currentId, Presets = remaining
        });
        userPresets.RemoveAll(preset => ids.Contains(preset.Id));
        if (ids.Contains(CurrentPresetId))
        {
            CurrentPresetId = WorkspaceLayoutPresets.STANDARD;
            if (!disposed)
            {
                UpdateModification(Capture());
            }
        }
        LastError = null;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    internal Task<bool> RestoreDefaultAsync()
    {
        return ApplyPresetAsync(WorkspaceLayoutPresets.STANDARD);
    }

    private void RefreshLanguage()
    {
        EnsureUsable();
        foreach (var panel in panels.Values)
        {
            panel.Title = GetPanelTitle(panel.Id);
        }
        RefreshFloatingTitles();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshLanguage();
    }

    internal void SetPanelUnreadCount(string panelId, int count)
    {
        EnsureUsable();
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var panel = GetPanel(panelId);
        if (unreadCounts.GetValueOrDefault(panelId) == count)
        {
            return;
        }
        unreadCounts[panelId] = count;
        panel.Title = GetPanelTitle(panelId);
        RefreshFloatingTitles();
    }

    private string GetPanelTitle(string panelId)
    {
        var title = Localization.Get("Layout." + (panelId));
        var count = unreadCounts.GetValueOrDefault(panelId);
        return count == 0 ? title : $"{title} ({count})";
    }

    internal async Task ShowManagerAsync()
    {
        EnsureUsable();
        var dialog = new LayoutPresetManagerWindow(this);
        RegisterAuxiliaryWindow(dialog);
        await dialog.ShowDialog(owner);
    }

    internal async Task ShowSaveAsAsync()
    {
        EnsureUsable();
        var dialog = new LayoutPresetNameWindow();
        RegisterAuxiliaryWindow(dialog);
        var name = await dialog.ShowDialog<string?>(owner);
        if (name is not null)
        {
            await SaveAsAsync(name);
        }
    }

    internal Task FlushAsync() => RunLayoutOperationAsync(FlushCoreAsync);

    private async Task<bool> FlushCoreAsync()
    {
        EnsureUsable();
        persistenceTimer.Stop();
        var current = Capture();
        UpdateModification(current);
        lastFingerprint = WorkspaceLayoutStore.Fingerprint(current);
        Changed?.Invoke(this, EventArgs.Empty);
        await PersistCurrentCoreAsync();
        await store.FlushAsync();
        return true;
    }

    internal WorkspaceLayoutFile CaptureFile()
    {
        EnsureUsable();
        return WorkspaceLayoutStore.Deserialize(WorkspaceLayoutStore.Serialize(new()
        {
            Current = Capture(),
            CurrentPresetId = CurrentPresetId,
            Presets = userPresets.ToArray()
        }));
    }

    internal WorkspaceLayoutSnapshot Capture()
    {
        EnsureUsable();
        var visible = EnumerateRoots().SelectMany(WorkbenchDockSnapshotCodec.Enumerate)
            .OfType<WorkbenchDockPanel>().Select(panel => panel.Id).ToHashSet(StringComparer.Ordinal);
        var floating = (root.Windows ?? []).Where(window => window.Layout is not null)
            .Select(window =>
            {
                var host = window.Host as Window;
                if (host?.IsVisible == true)
                {
                    window.Save();
                }
                return new LayoutFloatingSnapshot
                {
                    Content = codec.CaptureRoot(window.Layout!),
                    X = window.X, Y = window.Y,
                    Width = double.IsFinite(window.Width) && window.Width > 0 ? window.Width : 720,
                    Height = double.IsFinite(window.Height) && window.Height > 0 ? window.Height : 480,
                    Scaling = host?.IsVisible == true ? host.RenderScaling
                        : restoredWindowScales.GetValueOrDefault(window, owner.RenderScaling)
                };
            }).Where(window => window.Content.Children.Count > 0).ToArray();
        var focusedPanels = EnumerateRoots().Select(candidate => candidate.FocusedDockable)
            .OfType<WorkbenchDockPanel>().Where(panel => visible.Contains(panel.Id)).ToArray();
        var focus = focusedPanels.LastOrDefault(panel => panel.Owner is IDock { IsActive: true })?.Id
            ?? focusedPanels.FirstOrDefault()?.Id;
        var result = new WorkspaceLayoutSnapshot
        {
            Main = codec.CaptureRoot(root), Floating = floating,
            HiddenPanelIds = WorkbenchPanelIds.All.Where(id => !visible.Contains(id)).ToArray(),
            FocusedPanelId = focus
        };
        WorkspaceLayoutValidator.Validate(result);
        return result;
    }

    /// <summary>解绑空间观察并关闭布局窗口，工程和媒体资源由工作台会话释放。</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        Localization.LanguageChanged -= OnLanguageChanged;
        persistenceTimer.Stop();
        persistenceTimer.Tick -= OnPersistenceTimer;
        owner.Opened -= OnOwnerOpened;
        dockHost.RemoveHandler(InputElement.PointerPressedEvent, OnDockSpacePointerPressed);
        Unwatch();
        store.Dispose();
        applying = true;
        dockHost.Layout = null;
        CloseFloatingHosts();
        restoredWindowScales.Clear();
        foreach (var window in auxiliaryWindows.ToArray())
        {
            window.Close();
        }
        auxiliaryWindows.Clear();
        disposed = true;
    }

    private void ApplySnapshot(WorkspaceLayoutSnapshot snapshot)
    {
        WorkspaceLayoutValidator.Validate(snapshot);
        applying = true;
        try
        {
            persistenceTimer.Stop();
            Unwatch();
            dockHost.Layout = null;
            CloseFloatingHosts();
            restoredWindowScales.Clear();
            root = codec.BuildRoot(snapshot.Main);
            foreach (var id in snapshot.HiddenPanelIds)
            {
                root.HiddenDockables!.Add(panels[id]);
            }
            foreach (var floating in snapshot.Floating)
            {
                var screens = owner.Screens.All.Select(screen => new LayoutScreenArea(screen.WorkingArea.X,
                    screen.WorkingArea.Y, screen.WorkingArea.Width, screen.WorkingArea.Height, screen.Scaling)).ToArray();
                var bounds = LayoutWindowBounds.Clamp(floating, screens);
                var window = factory.CreateDockWindow();
                window.Layout = codec.BuildRoot(floating.Content);
                window.Layout.Window = window;
                window.X = bounds.X;
                window.Y = bounds.Y;
                window.Width = bounds.Width;
                window.Height = bounds.Height;
                window.Title = string.Empty;
                restoredWindowScales.Add(window, bounds.Scaling);
                root.Windows!.Add(window);
            }
            factory.InitDockable(root, null);
            dockHost.Layout = root;
            if (snapshot.FocusedPanelId is { } focused && IsVisible(focused))
            {
                factory.SetActiveDockable(panels[focused]);
                if (panels[focused].Owner is IDock dock)
                {
                    factory.SetFocusedDockable(dock, panels[focused]);
                }
            }
            if (owner.IsVisible)
            {
                PresentFloatingHosts();
            }
            Watch();
            var current = Capture();
            lastFingerprint = WorkspaceLayoutStore.Fingerprint(current);
            UpdateModification(current);
        }
        finally
        {
            applying = false;
        }
    }

    private WorkbenchFloatingHostWindow CreateFloatingHost()
    {
        var host = new WorkbenchFloatingHostWindow(factory, OnFloatingClosing, ScheduleCapture);
        host.DockHost.AddHandler(InputElement.PointerPressedEvent, OnDockSpacePointerPressed, RoutingStrategies.Tunnel, true);
        floatingHosts.Add(host);
        registerWindow(host);
        host.Closed += (_, _) =>
        {
            host.DockHost.RemoveHandler(InputElement.PointerPressedEvent, OnDockSpacePointerPressed);
            floatingHosts.Remove(host);
        };
        return host;
    }

    private void RegisterAuxiliaryWindow(Window window)
    {
        auxiliaryWindows.Add(window);
        registerWindow(window);
        window.Closed += (_, _) => auxiliaryWindows.Remove(window);
    }

    private bool OnFloatingClosing(WorkbenchFloatingHostWindow host)
    {
        cancelGestures();
        if (applying || host.Window?.Layout is not { } floatingRoot)
        {
            return true;
        }
        if (WorkbenchDockSnapshotCodec.Enumerate(floatingRoot).OfType<WorkbenchDockPanel>()
            .Any(panel => panel.Id == WorkbenchPanelIds.SUBTITLE_DETAILS) && !commitDrafts())
        {
            SetError(Localization.Get("Layout.InvalidDraft"));
            return false;
        }
        var hidden = WorkbenchDockSnapshotCodec.Enumerate(floatingRoot).OfType<WorkbenchDockPanel>()
            .Concat((floatingRoot.HiddenDockables ?? []).OfType<WorkbenchDockPanel>()).Distinct().ToArray();
        foreach (var panel in hidden)
        {
            if (panel.Owner is IDock oldOwner)
            {
                oldOwner.VisibleDockables?.Remove(panel);
                if (oldOwner.ActiveDockable == panel)
                {
                    oldOwner.ActiveDockable = oldOwner.VisibleDockables?.FirstOrDefault();
                }
            }
            floatingRoot.HiddenDockables?.Remove(panel);
            root.HiddenDockables ??= factory.CreateList<IDockable>();
            if (!root.HiddenDockables.Contains(panel))
            {
                root.HiddenDockables.Add(panel);
            }
            panel.OriginalOwner = null;
            panel.Owner = root;
        }
        root.Windows?.Remove(host.Window);
        restoredWindowScales.Remove(host.Window);
        ScheduleCapture();
        return true;
    }

    private void CloseFloatingHosts()
    {
        foreach (var host in floatingHosts.ToArray())
        {
            host.IsApplyingLayout = true;
            host.IsTracked = false;
            host.DockHost.Layout = null;
            if (host.Window is { } model)
            {
                model.Layout = null;
                model.Owner = null;
            }
            host.Close();
        }
        floatingHosts.Clear();
    }

    private IToolDock FindReopenDock()
    {
        var dock = WorkbenchDockSnapshotCodec.Enumerate(root).OfType<IToolDock>().FirstOrDefault();
        if (dock is not null)
        {
            return dock;
        }
        dock = factory.CreateToolDock();
        dock.Id = "workspace-reopened-" + Guid.NewGuid().ToString("N");
        dock.VisibleDockables = factory.CreateList<IDockable>();
        factory.AddDockable(root, dock);
        root.ActiveDockable = dock;
        return dock;
    }

    private IEnumerable<IRootDock> EnumerateRoots()
    {
        yield return root;
        foreach (var window in root.Windows ?? [])
        {
            if (window.Layout is { } floatingRoot)
            {
                yield return floatingRoot;
            }
        }
    }

    private WorkbenchDockPanel GetPanel(string id)
    {
        return panels.TryGetValue(id, out var panel) ? panel : throw new ArgumentException("Unknown panel ID.", nameof(id));
    }

    private void ScheduleCapture()
    {
        if (applying || disposed)
        {
            return;
        }
        persistenceTimer.Stop();
        persistenceTimer.Start();
    }

    private void OnDockSpacePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual visual)
        {
            var ancestors = visual.GetVisualAncestors()
                .Concat(visual is ILogical logical ? logical.GetLogicalAncestors().OfType<Visual>() : []).ToArray();
            if (panels.Values.Any(panel => ReferenceEquals(panel.View, visual) || ancestors.Contains(panel.View)))
            {
                return;
            }
        }
        cancelGestures();
    }

    private async void OnPersistenceTimer(object? sender, EventArgs e)
    {
        persistenceTimer.Stop();
        try
        {
            foreach (var host in floatingHosts.ToArray())
            {
                if (host.IsVisible && host.Window?.Layout is { } floatingRoot
                    && !WorkbenchDockSnapshotCodec.Enumerate(floatingRoot).OfType<WorkbenchDockPanel>().Any())
                {
                    host.Close();
                }
            }
            Watch();
            RefreshFloatingTitles();
            var layout = Capture();
            var fingerprint = WorkspaceLayoutStore.Fingerprint(layout);
            if (fingerprint == lastFingerprint)
            {
                return;
            }
            lastFingerprint = fingerprint;
            UpdateModification(layout);
            Changed?.Invoke(this, EventArgs.Empty);
            await PersistCurrentAsync();
        }
        catch (ObjectDisposedException) when (disposed)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            SetError(exception.Message);
        }
    }

    private Task<bool> PersistCurrentAsync() => RunLayoutOperationAsync(async () =>
    {
        await PersistCurrentCoreAsync();
        return true;
    });

    private async Task<T> RunLayoutOperationAsync<T>(Func<Task<T>> action)
    {
        EnsureUsable();
        await persistenceGate.WaitAsync();
        try
        {
            EnsureUsable();
            return await action();
        }
        finally
        {
            persistenceGate.Release();
        }
    }

    private Task PersistCurrentCoreAsync()
    {
        return store.SaveAsync(new()
        {
            Current = Capture(), CurrentPresetId = CurrentPresetId, Presets = userPresets.ToArray()
        });
    }

    private void UpdateModification(WorkspaceLayoutSnapshot current)
    {
        var preset = Presets.Single(candidate => candidate.Id == CurrentPresetId);
        IsModified = WorkspaceLayoutStore.Fingerprint(current) != WorkspaceLayoutStore.Fingerprint(preset.Layout);
    }

    private bool IsValidName(string name, string? excludingId)
    {
        return name.Length is > 0 and <= 80 && !name.Any(char.IsControl)
            && !userPresets.Any(preset => preset.Id != excludingId && string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private void SetError(string error)
    {
        LastError = error;
        Error?.Invoke(this, EventArgs.Empty);
    }

    private void OnOwnerOpened(object? sender, EventArgs e)
    {
        PresentFloatingHosts();
    }

    private void PresentFloatingHosts()
    {
        foreach (var window in root.Windows ?? [])
        {
            window.Present(false);
        }
        RefreshFloatingTitles();
    }

    private void RefreshFloatingTitles()
    {
        foreach (var host in floatingHosts.ToArray())
        {
            var panel = host.Window?.Layout is { } floatingRoot
                ? WorkbenchDockSnapshotCodec.Enumerate(floatingRoot).OfType<IToolDock>()
                    .Select(dock => dock.ActiveDockable).OfType<WorkbenchDockPanel>().FirstOrDefault()
                : null;
            var title = panel?.Title ?? Localization.Get("Layout.Layout");
            host.Title = title;
            FloatingWindowTitleChanged?.Invoke(host, title);
        }
    }

    private void Watch()
    {
        Unwatch();
        foreach (var candidate in EnumerateRoots())
        {
            foreach (var dockable in WorkbenchDockSnapshotCodec.Enumerate(candidate))
            {
                if (dockable is INotifyPropertyChanged properties && !watchedProperties.Contains(properties))
                {
                    properties.PropertyChanged += OnDockPropertyChanged;
                    watchedProperties.Add(properties);
                }
                if (dockable is IDock dock && dock.VisibleDockables is INotifyCollectionChanged collection)
                {
                    collection.CollectionChanged += OnDockCollectionChanged;
                    watchedCollections.Add(collection);
                }
            }
            if (candidate.Windows is INotifyCollectionChanged windows)
            {
                windows.CollectionChanged += OnDockCollectionChanged;
                watchedCollections.Add(windows);
            }
            foreach (var window in candidate.Windows ?? [])
            {
                if (window is INotifyPropertyChanged properties)
                {
                    properties.PropertyChanged += OnDockPropertyChanged;
                    watchedProperties.Add(properties);
                }
            }
        }
    }

    private void Unwatch()
    {
        foreach (var properties in watchedProperties)
        {
            properties.PropertyChanged -= OnDockPropertyChanged;
        }
        foreach (var collection in watchedCollections)
        {
            collection.CollectionChanged -= OnDockCollectionChanged;
        }
        watchedProperties.Clear();
        watchedCollections.Clear();
    }

    private void OnDockPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "Proportion" or "ActiveDockable" or "FocusedDockable" or "VisibleDockables"
            or "X" or "Y" or "Width" or "Height")
        {
            ScheduleCapture();
        }
    }

    private void OnDockCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScheduleCapture();
    }

    private void EnsureUsable()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Dispatcher.UIThread.VerifyAccess();
    }
}
