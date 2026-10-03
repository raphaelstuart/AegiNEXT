using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;

namespace AegiNext.Desktop.Layouts;

internal sealed class WorkbenchDockFactory : Factory
{
    private readonly Action cancelGestures;
    private readonly Action changed;

    internal WorkbenchDockFactory(Func<IHostWindow?> createHost, Action cancelGestures, Action changed)
    {
        this.cancelGestures = cancelGestures;
        this.changed = changed;
        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>> { [nameof(IDockWindow)] = createHost };
        HideToolsOnClose = true;
    }

    /// <summary>创建立即呈现的功能标签组，并允许最后一个面板隐藏。</summary>
    public override IToolDock CreateToolDock()
    {
        var result = new WorkbenchToolDock();
        result.CanCloseLastDockable = true;
        result.CanPin = false;
        return result;
    }

    /// <summary>创建立即呈现的工作区根，初始化 Dock 所需的固定区集合。</summary>
    public override IRootDock CreateRootDock()
    {
        return new WorkbenchRootDock
        {
            LeftPinnedDockables = CreateList<IDockable>(),
            RightPinnedDockables = CreateList<IDockable>(),
            TopPinnedDockables = CreateList<IDockable>(),
            BottomPinnedDockables = CreateList<IDockable>()
        };
    }

    /// <summary>创建按比例分割的空间容器，不复制其中的功能面板。</summary>
    public override IProportionalDock CreateProportionalDock()
    {
        return new WorkbenchProportionalDock();
    }

    /// <summary>面板脱离空间时取消手势，并安排捕获最新布局。</summary>
    public override void OnDockableUndocked(IDockable? dockable, DockOperation operation)
    {
        cancelGestures();
        base.OnDockableUndocked(dockable, operation);
        changed();
    }

    /// <summary>关闭或隐藏面板前取消正在进行的编辑手势。</summary>
    public override bool OnDockableClosing(IDockable? dockable)
    {
        cancelGestures();
        return base.OnDockableClosing(dockable);
    }

    /// <summary>停靠完成后安排捕获最新空间拓扑。</summary>
    public override void OnDockableDocked(IDockable? dockable, DockOperation operation)
    {
        base.OnDockableDocked(dockable, operation);
        changed();
    }

    /// <summary>面板隐藏后安排保存显隐状态。</summary>
    public override void OnDockableHidden(IDockable? dockable)
    {
        base.OnDockableHidden(dockable);
        changed();
    }

    /// <summary>面板恢复后安排保存所在标签组及显隐状态。</summary>
    public override void OnDockableRestored(IDockable? dockable)
    {
        base.OnDockableRestored(dockable);
        changed();
    }

    /// <summary>面板移动后安排保存标签顺序及空间拓扑。</summary>
    public override void OnDockableMoved(IDockable? dockable)
    {
        base.OnDockableMoved(dockable);
        changed();
    }

    /// <summary>浮窗创建后安排保存窗口及面板布局。</summary>
    public override void OnWindowAdded(IDockWindow? window)
    {
        base.OnWindowAdded(window);
        changed();
    }

    /// <summary>浮窗移除后安排保存面板的恢复或隐藏结果。</summary>
    public override void OnWindowRemoved(IDockWindow? window)
    {
        base.OnWindowRemoved(window);
        changed();
    }
}
