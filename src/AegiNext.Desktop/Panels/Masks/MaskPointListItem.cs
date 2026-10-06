using System.Globalization;
using AegiNext.Core.Projects;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Panels.Masks;

internal sealed class MaskPointListItem : ObservableObject
{
    private int index;
    private ScenePoint position;
    private string label;

    internal MaskPointListItem(Guid id, int index, ScenePoint position)
    {
        Id = id;
        this.index = index;
        this.position = position;
        label = FormatLabel(index, position);
    }

    public Guid Id { get; }
    public int Index => index;
    public ScenePoint Position => position;
    public string Label => label;

    internal void Update(int valueIndex, ScenePoint valuePosition)
    {
        SetProperty(ref index, valueIndex, nameof(Index));
        SetProperty(ref position, valuePosition, nameof(Position));
        SetProperty(ref label, FormatLabel(valueIndex, valuePosition), nameof(Label));
    }

    private static string FormatLabel(int index, ScenePoint position) => string.Create(CultureInfo.CurrentCulture,
        $"{index}   ({position.X:0.###}, {position.Y:0.###})");
}
