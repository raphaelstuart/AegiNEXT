using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace AegiNext.Desktop.Styling;

internal static class WorkbenchIcon
{
    private static readonly Dictionary<string, string> paths = new(StringComparer.Ordinal)
    {
        ["File"] = "M6 2H14L20 8V22H6Z M14 4V9H19V8Z M8 12V14H18V12Z M8 16V18H16V16Z",
        ["Open"] = "M2 5H10L12 7H22V10H6L2 20Z M6 12H23L19 21H2Z",
        ["Save"] = "M3 3H19L22 6V22H3Z M7 3V10H17V3Z M7 14V22H18V14Z M13 4H16V9H13Z",
        ["SaveAs"] = "M3 3H19L22 6V10L11 21H3Z M7 3V10H17V3Z M7 14V19H10L15 14Z M13 21L14 17L20 11L23 14L17 20Z",
        ["ManageLayouts"] = "M2 3H22V21H2Z M4 5V19H8V5Z M10 5V11H20V5Z M10 13V19H20V13Z",
        ["RestoreLayout"] = "M12 3A9 9 0 1 1 3 12H6A6 6 0 1 0 12 6H10V10L4 5L10 0V3Z",
        ["Add"] = "M11 3H13V11H21V13H13V21H11V13H3V11H11Z",
        ["Delete"] = "M8 3H16V5H21V7H3V5H8Z M5 9H19L18 22H6Z M8 10V20H10V10Z M14 10V20H16V10Z",
        ["Undo"] = "M8 3L1 9L8 15V11H14C18 11 20 14 20 19H22C22 12 19 8 14 8H8Z",
        ["Redo"] = "M16 3L23 9L16 15V11H10C6 11 4 14 4 19H2C2 12 5 8 10 8H16Z",
        ["Import"] = "M3 3H14V6H6V20H18V14H21V23H3Z M14 7H20V3L24 8L20 13V9H14Z",
        ["Export"] = "M3 3H14V6H6V20H18V14H21V23H3Z M15 7V4H23V12H20V9L12 17L10 15L18 7Z",
        ["Settings"] =
            "M9.79 4.31L10.44 2.12L13.56 2.12L14.21 4.31L15.88 5L17.88 3.91L20.09 6.12L19 8.12L19.69 9.79L21.88 10.44L21.88 13.56L19.69 14.21L19 15.88L20.09 17.88L17.88 20.09L15.88 19L14.21 19.69L13.56 21.88L10.44 21.88L9.79 19.69L8.12 19L6.12 20.09L3.91 17.88L5 15.88L4.31 14.21L2.12 13.56L2.12 10.44L4.31 9.79L5 8.12L3.91 6.12L6.12 3.91L8.12 5Z M12 8A4 4 0 1 0 12 16A4 4 0 1 0 12 8Z",
        ["Play"] = "M6 3V21L21 12Z",
        ["Pause"] = "M5 3H10V21H5Z M14 3H19V21H14Z",
        ["Loop"] = "M5 5H18V2L23 7L18 12V9H5V13H1V9A4 4 0 0 1 5 5Z M19 19H6V22L1 17L6 12V15H19V11H23V15A4 4 0 0 1 19 19Z",
        ["EnableHighlight"] = "M2 4H22V20H2Z M5 8H19V10H5Z M5 12H10V16H5Z M12 12H19V16H12Z",
        ["HighlightStyle"] = "M9 2H15L22 18H18L16 13H8L6 18H2Z M9 10H15L12 4Z M2 21H22V24H2Z",
        ["Backward"] = "M12 4V20L2 12Z M22 4V20L12 12Z",
        ["Forward"] = "M2 4V20L12 12Z M12 4V20L22 12Z",
        ["Enter"] = "M3 3H6V21H3Z M9 5V19L21 12Z",
        ["ExitTiming"] = "M18 3H21V21H18Z M3 5V19L15 12Z",
        ["Split"] = "M11 2H13V7H11Z M11 17H13V22H11Z M3 8H9V16H3Z M15 8H21V16H15Z",
        ["Merge"] = "M2 5H7V19H2Z M17 5H22V19H17Z M8 8L12 12L8 16Z M16 8L12 12L16 16Z",
        ["Style"] = "M8 3H16L23 21H19L16 15H8L5 21H1Z M9 12H15L12 6Z",
        ["Effects"] = "M13 1L10 9H2L8 14L5 23L13 17L21 23L18 14L24 9H16Z",
        ["Subtitles"] = "M2 4H22V20H2Z M5 8V10H19V8Z M5 12V14H11V12Z M13 12V14H19V12Z M5 16V18H16V16Z",
        ["SubtitleEditor"] = "M2 3H22V10H19V6H5V20H10V23H2Z M7 8H17V10H7Z M7 12H14V14H7Z M13 21L14 17L20 11L23 14L17 20Z",
        ["View"] =
            "M12 4C6 4 2 8 0 12C2 16 6 20 12 20C18 20 22 16 24 12C22 8 18 4 12 4Z M12 7A5 5 0 1 0 12 17A5 5 0 1 0 12 7Z M12 10A2 2 0 1 1 12 14A2 2 0 1 1 12 10Z",
        ["Timeline"] = "M2 3H4V21H2Z M7 5H20V9H7Z M11 11H23V15H11Z M6 17H17V21H6Z",
        ["Magnet"] = "M3 2H8V7H3Z M16 2H21V7H16Z M3 9H8V13A4 4 0 0 0 16 13V9H21V13A9 9 0 0 1 3 13Z",
        ["Spectrum"] = "M2 11H5V21H2Z M7 5H10V21H7Z M12 2H15V21H12Z M17 7H20V21H17Z M22 14H24V21H22Z",
        ["Waveform"] = "M1 11H4V13H1Z M5 7H7V17H5Z M9 2H11V22H9Z M13 5H15V19H13Z M17 9H19V15H17Z M21 11H24V13H21Z",
        ["Close"] = "M5 3L12 10L19 3L21 5L14 12L21 19L19 21L12 14L5 21L3 19L10 12L3 5Z",
        ["Up"] = "M3 14L12 5L21 14L19 16L12 9L5 16Z",
        ["Down"] = "M3 10L12 19L21 10L19 8L12 15L5 8Z",
        ["Rectangle"] = "M2 4H22V20H2Z M5 7V17H19V7Z",
        ["Ellipse"] = "M12 2A10 10 0 1 0 12 22A10 10 0 1 0 12 2Z M12 5A7 7 0 1 1 12 19A7 7 0 1 1 12 5Z",
        ["Image"] = "M2 3H22V21H2Z M5 6V16L10 11L14 15L17 12L20 16V6Z M15 8A2 2 0 1 0 19 8A2 2 0 1 0 15 8Z",
        ["Keyframe"] = "M12 2L22 12L12 22L2 12Z",
        ["Path"] = "M3 3H8V8H3Z M16 16H21V21H16Z M7 10C18 5 7 19 16 14L18 16C7 23 16 9 7 13Z",
        ["Mask"] = "M2 4H22V17L12 23L2 17Z M5 8V11H10V8Z M14 8V11H19V8Z M8 15V17H16V15Z",
        ["Group"] = "M2 2H14V6H6V14H2Z M10 10H22V22H10Z M13 13V19H19V13Z",
        ["Bold"] =
            "M5 3H13C20 3 21 10 17 12C23 16 19 22 13 22H5Z M9 6V10H13C17 10 17 6 13 6Z M9 14V18H14C18 18 18 14 14 14Z",
        ["Italic"] = "M10 3H22V6H17L11 18H16V21H4V18H7L13 6H10Z",
        ["ApplySelectionPreset"] = "M3 2H15V5H6V19H11V22H3Z M16 7H19V11H23V14H19V18H16V14H12V11H16Z",
        ["ApplySelectionStyle"] = "M7 3H11L17 17H13L11 12H7L5 17H1Z M8 9H10L9 6Z M14 20L17 23L24 15L22 13L17 19L16 18Z",
        ["ClearSelectionStyle"] = "M11 3L22 14L14 22H7L1 16Z M11 7L5 13L11 19H13L18 14Z M5 22H22V24H5Z",
        ["Volume"] = "M2 8H7L13 3V21L7 16H2Z M16 5C22 8 22 16 16 19V16C19 14 19 10 16 8Z",
        ["Mute"] = "M2 8H7L13 3V21L7 16H2Z M17 8L20 11L23 8L24 9L21 12L24 15L23 16L20 13L17 16L16 15L19 12L16 9Z",
        ["Keyboard"] = "M2 5H22V19H2Z M4 7V17H20V7Z M6 9H8V11H6Z M10 9H12V11H10Z M14 9H16V11H14Z M6 13H18V15H6Z"
    };

    internal static PathIcon Create(string key, double size = 16)
    {
        return new() { Data = Geometry.Parse(GetPath(key)), Width = size, Height = size };
    }

    internal static StackPanel Content(string text, string key)
    {
        return new()
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Children =
            {
                Create(key),
                new TextBlock
                    { Text = text, FontSize = 13, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }
            }
        };
    }

    internal static Bitmap CreateNative(string key)
    {
        using var bitmap = new SKBitmap(24, 24, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        using var path = SKPath.ParseSvgPathData(GetPath(key));
        path.FillType = SKPathFillType.EvenOdd;
        using var paint = new SKPaint { Color = new(128, 128, 128), IsAntialias = true };
        canvas.DrawPath(path, paint);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = data.AsStream();
        return new(stream);
    }

    private static string GetPath(string key)
    {
        var resolved = key switch
        {
            "New" or "NEW_PROJECT" or "File" => "File",
            "OpenProject" or "OPEN_PROJECT" or "OPEN_MEDIA" => "Open",
            "SavePreset" or "SAVE_PROJECT" or "SAVE_PROJECT_AS" or "LAYOUT_SAVE" => "Save",
            "LAYOUT_SAVE_AS" => "SaveAs",
            "LAYOUT_MANAGE" => "ManageLayouts",
            "LAYOUT_RESTORE_DEFAULT" => "RestoreLayout",
            "AddLayer" or "ADD_SUBTITLE" => "Add",
            "DeleteKeyframe" or "DELETE_SUBTITLE" => "Delete",
            "UNDO" or "Edit" => "Undo", "REDO" => "Redo",
            "ImportFont" or "IMPORT_SUBTITLES" or "IMPORT_ASS" => "Import",
            "ExportText" or "EXPORT_SUBTITLES" or "EXPORT_ASS" or "EXPORT_VIDEO" or "VIEW_EXPORT" => "Export",
            "OPEN_SETTINGS" or "ManageStyles" => "Settings",
            "PLAY_PAUSE" or "Playback" => "Play",
            "SEEK_BACKWARD" => "Backward", "SEEK_FORWARD" => "Forward",
            "SetStart" or "TIMING_ENTER" => "Enter", "SetEnd" or "TIMING_EXIT" => "ExitTiming",
            "SPLIT_SUBTITLE" => "Split", "MERGE_SUBTITLE" => "Merge",
            "VIEW_STYLES" or "ApplyStyle" => "Style", "VIEW_EFFECTS" => "Effects",
            "OPEN_SUBTITLE_DETAILS" => "SubtitleEditor",
            "VIEW_TIMELINE" => "Timeline", "EXIT" or "Cancel" => "Close",
            "Ungroup" => "Group", "ClearPath" => "Path", "ClearMask" => "Mask",
            "Record" or "Shortcuts" => "Keyboard", "Clear" => "Close", "Reset" => "Undo",
            "Duplicate" => "File", "InvertMask" => "Mask", "OrientPath" => "Path",
            "Karaoke" or "ClearKaraoke" => "Subtitles",
            "ApplyPreset" or "Fade" or "Pop" or "Slide" => "Effects",
            _ => key
        };
        return paths.GetValueOrDefault(resolved, paths["Settings"]);
    }
}
