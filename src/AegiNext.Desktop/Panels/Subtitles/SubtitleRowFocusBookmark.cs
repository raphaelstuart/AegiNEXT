using Avalonia.Controls;

namespace AegiNext.Desktop.Panels.Subtitles;

internal sealed record SubtitleRowFocusBookmark(
    Guid RowId,
    int Column,
    TopLevel Root,
    int Revision,
    int CaretIndex,
    int SelectionStart,
    int SelectionEnd);
