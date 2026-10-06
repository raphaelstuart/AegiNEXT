using System.Collections;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;

namespace AegiNext.Desktop.Ui.Tests;

internal sealed class CountingTextStyles(IReadOnlyList<ValueSpan<TextRunProperties>> values) : IReadOnlyList<ValueSpan<TextRunProperties>>
{
    public int Reads { get; private set; }
    public int Count => values.Count;
    public ValueSpan<TextRunProperties> this[int index]
    {
        get
        {
            Reads++;
            return values[index];
        }
    }

    public IEnumerator<ValueSpan<TextRunProperties>> GetEnumerator() => values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
