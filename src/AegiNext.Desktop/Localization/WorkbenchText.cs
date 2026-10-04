using System.Globalization;
using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Localization;

internal static class WorkbenchText
{
    private static readonly Dictionary<string, (string Chinese, string English)> entries = new(StringComparer.Ordinal)
    {
        ["EditKeyframeTarget"] = ("编辑关键帧", "Editing keyframe"),
        ["EditPlayheadTarget"] = ("编辑播放头时刻", "Editing playhead time"),
        ["SubtitleTrack"] = ("字幕轨道", "Subtitle track"),
        ["AddTrack"] = ("新增轨道", "Add track"),
        ["RenameTrack"] = ("重命名轨道", "Rename track"),
        ["DeleteTrack"] = ("删除空轨道", "Delete empty track"),
        ["MoveTrackUp"] = ("轨道上移", "Move track up"),
        ["MoveTrackDown"] = ("轨道下移", "Move track down"),
        ["MoveToTrack"] = ("移动至轨道", "Move to track"),
        ["Open"] = ("打开视频", "Open video"),
        ["New"] = ("新建", "New"), ["OpenProject"] = ("打开工程", "Open project"),
        ["File"] = ("文件", "File"), ["Edit"] = ("编辑", "Edit"), ["View"] = ("视图", "View"),
        ["Playback"] = ("播放", "Playback"), ["Settings"] = ("设置", "Settings"),
        ["Layouts"] = ("布局", "Layouts"), ["LayoutModified"] = ("已修改", "Modified"),
        ["Preview"] = ("视频预览", "Preview"),
        ["StylePreset"] = ("字幕样式预设", "Subtitle style preset"),
        ["ApplyStyle"] = ("套用", "Apply"), ["ManageStyles"] = ("管理预设", "Manage presets"),
        ["StyleFiles"] = ("字幕样式预设", "Subtitle style presets"),
        ["ImportStyles"] = ("导入样式预设", "Import style presets"),
        ["ExportStyles"] = ("导出样式预设", "Export style presets"),
        ["CapturedStyle"] = ("字幕样式", "Subtitle style"),
        ["SpaceKey"] = ("空格", "Space"),
        ["Save"] = ("保存", "Save"), ["SaveAs"] = ("另存为", "Save as"),
        ["Import"] = ("导入字幕", "Import subtitles"), ["ExportText"] = ("导出字幕", "Export subtitles"),
        ["Export"] = ("压制", "Encode"), ["Cancel"] = ("取消", "Cancel"),
        ["Undo"] = ("撤销", "Undo"), ["Redo"] = ("重做", "Redo"),
        ["Add"] = ("新增", "Add"), ["Delete"] = ("删除", "Delete"),
        ["Split"] = ("拆分", "Split"), ["Merge"] = ("合并", "Merge"),
        ["Start"] = ("开始", "Start"), ["End"] = ("结束", "End"),
        ["Duration"] = ("时长", "Duration"), ["Text"] = ("字幕内容", "Subtitle text"),
        ["Subtitles"] = ("字幕", "Subtitles"), ["Effects"] = ("特效", "Effects"),
        ["Style"] = ("样式", "Style"), ["Layers"] = ("图层", "Layers"),
        ["Font"] = ("字体", "Font"), ["Size"] = ("字号", "Size"),
        ["ImportFont"] = ("导入字体", "Import font"), ["Fonts"] = ("字体文件", "Font files"),
        ["SplitCaret"] = ("先在字幕文字中定位拆分位置。", "Place the caret at the text split first."),
        ["EaseIn"] = ("缓入", "Ease in"), ["EaseOut"] = ("缓出", "Ease out"), ["NoAudio"] = ("无音频", "No audio"),
        ["Fill"] = ("颜色", "Color"), ["Stroke"] = ("描边", "Outline"),
        ["StrokeWidth"] = ("描边宽度", "Outline width"), ["Alignment"] = ("对齐", "Alignment"),
        ["ExplicitPosition"] = ("自定义锚点位置", "Custom anchored position"),
        ["AnchorPreset"] = ("锚点预设", "Anchor presets"),
        ["AnchorPresetHint"] = ("点击保留文字位置；Shift 同时设置轴心；Alt 将偏移归零。", "Click preserves the text position; Shift also sets the pivot; Alt resets offsets to zero."),
        ["SubtitlePreviewText"] = ("字幕预览", "Subtitle Preview"),
        ["SubtitlePositionUnavailable"] = ("字幕位置无法测量，请修复字体资源或文字后重试", "Subtitle position cannot be measured. Repair the font asset or text to continue"),
        ["SubtitlePositionHint"] = ("锚点相对画布，轴心相对实际文字。0 为左／上，1 为右／下；偏移单位为像素。", "Anchor follows the canvas; pivot follows the glyph bounds. 0 is left/top, 1 is right/bottom; offsets use pixels."),
        ["AnchorX"] = ("锚点 X", "Anchor X"), ["AnchorY"] = ("锚点 Y", "Anchor Y"),
        ["PivotX"] = ("轴心 X", "Pivot X"), ["PivotY"] = ("轴心 Y", "Pivot Y"),
        ["OffsetX"] = ("偏移 X（像素）", "Offset X (pixels)"), ["OffsetY"] = ("偏移 Y（像素）", "Offset Y (pixels)"),
        ["Bold"] = ("粗体", "Bold"), ["Italic"] = ("斜体", "Italic"),
        ["Karaoke"] = ("逐字高亮", "Karaoke"), ["ClearKaraoke"] = ("清除高亮", "Clear karaoke"),
        ["AddLayer"] = ("字幕图层", "Subtitle layer"), ["Rectangle"] = ("矩形", "Rectangle"),
        ["Ellipse"] = ("椭圆", "Ellipse"), ["Image"] = ("图片", "Image"),
        ["Group"] = ("分组", "Group"), ["Ungroup"] = ("解组", "Ungroup"),
        ["Up"] = ("上移", "Up"), ["Down"] = ("下移", "Down"),
        ["Name"] = ("名称", "Name"), ["PositionX"] = ("位置 X", "Position X"),
        ["PositionY"] = ("位置 Y", "Position Y"), ["ScaleX"] = ("缩放 X", "Scale X"),
        ["ScaleY"] = ("缩放 Y", "Scale Y"), ["Rotation"] = ("旋转", "Rotation"),
        ["Opacity"] = ("透明度", "Opacity"), ["Blur"] = ("模糊", "Blur"),
        ["Blend"] = ("混合", "Blend"), ["Normal"] = ("正常", "Normal"),
        ["Multiply"] = ("正片叠底", "Multiply"), ["Screen"] = ("滤色", "Screen"),
        ["AddBlend"] = ("相加", "Add"), ["Overlay"] = ("叠加", "Overlay"),
        ["Darken"] = ("变暗", "Darken"), ["Lighten"] = ("变亮", "Lighten"),
        ["Difference"] = ("差值", "Difference"), ["Path"] = ("编辑路径", "Edit path"),
        ["Mask"] = ("编辑蒙版", "Edit mask"), ["ClearPath"] = ("清除路径", "Clear path"),
        ["ClearMask"] = ("清除蒙版", "Clear mask"), ["InvertMask"] = ("反向蒙版", "Invert mask"),
        ["OrientPath"] = ("沿路径旋转", "Orient to path"), ["Property"] = ("属性", "Property"),
        ["Value"] = ("值", "Value"), ["Keyframe"] = ("添加关键帧", "Add keyframe"),
        ["DeleteKeyframe"] = ("删除关键帧", "Delete keyframe"), ["Interpolation"] = ("插值", "Interpolation"),
        ["Linear"] = ("线性", "Linear"), ["Hold"] = ("保持", "Hold"), ["Smooth"] = ("缓入缓出", "Ease in/out"),
        ["Preset"] = ("预设", "Preset"), ["SavePreset"] = ("保存预设", "Save preset"),
        ["ApplyPreset"] = ("应用预设", "Apply preset"), ["Fade"] = ("淡入淡出", "Fade in/out"),
        ["Pop"] = ("弹入", "Pop in"), ["Slide"] = ("滑入", "Slide in"),
        ["Codec"] = ("视频编码", "Video codec"), ["Automatic"] = ("自动", "Automatic"),
        ["Speed"] = ("速度", "Speed"), ["Fast"] = ("快速", "Fast"),
        ["Medium"] = ("标准", "Medium"), ["Slow"] = ("精细", "Slow"),
        ["Audio"] = ("音频", "Audio"), ["Copy"] = ("复制原音轨", "Copy audio"),
        ["AudioBitrate"] = ("音频码率", "Audio bitrate"), ["Quality"] = ("质量 CRF", "Quality CRF"),
        ["Theme"] = ("外观", "Theme"), ["System"] = ("跟随系统", "System"),
        ["Light"] = ("浅色", "Light"), ["Dark"] = ("深色", "Dark"),
        ["Language"] = ("语言", "Language"), ["Volume"] = ("音量", "Volume"),
        ["Mute"] = ("静音", "Mute"), ["Untitled"] = ("未命名工程", "Untitled project"),
        ["Analyzing"] = ("正在分析音频…", "Analyzing audio…"), ["Ready"] = ("就绪", "Ready"),
        ["Saved"] = ("已保存", "Saved"), ["Exported"] = ("压制完成", "Encode complete"),
        ["Cancelled"] = ("已取消", "Cancelled"), ["NoSelection"] = ("请先选择字幕或图层。", "Select a subtitle or layer first."),
        ["Projects"] = ("AegiNext 工程", "AegiNext project"), ["SubtitleFiles"] = ("字幕文件", "Subtitle files"),
        ["Images"] = ("图片文件", "Image files"), ["Videos"] = ("视频文件", "Video files"),
        ["UnsavedTitle"] = ("保存修改？", "Save changes?"),
        ["UnsavedText"] = ("工程有未保存的修改。", "The project has unsaved changes."),
        ["Discard"] = ("不保存", "Discard"), ["Zoom"] = ("缩放", "Zoom"),
        ["SetStart"] = ("进入", "Enter"), ["SetEnd"] = ("退出", "Exit"),
        ["FillRed"] = ("颜色 R", "Color R"), ["FillGreen"] = ("颜色 G", "Color G"),
        ["FillBlue"] = ("颜色 B", "Color B"), ["FillAlpha"] = ("颜色 Alpha", "Color alpha"),
        ["StrokeRed"] = ("描边 R", "Outline R"), ["StrokeGreen"] = ("描边 G", "Outline G"),
        ["StrokeBlue"] = ("描边 B", "Outline B"), ["StrokeAlpha"] = ("描边 Alpha", "Outline alpha"),
        ["PathProgress"] = ("路径进度", "Path progress"),
        ["BottomCenter"] = ("底部居中", "Bottom center"), ["TopCenter"] = ("顶部居中", "Top center"),
        ["TopLeft"] = ("顶部左侧", "Top left"), ["TopRight"] = ("顶部右侧", "Top right"),
        ["MiddleLeft"] = ("中部左侧", "Middle left"), ["MiddleCenter"] = ("中部居中", "Middle center"),
        ["MiddleRight"] = ("中部右侧", "Middle right"),
        ["Center"] = ("居中", "Center"), ["BottomLeft"] = ("底部左侧", "Bottom left"),
        ["BottomRight"] = ("底部右侧", "Bottom right")
    };

    internal static string Get(string key)
    {
        var value = entries[key];
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? value.Chinese : value.English;
    }

    internal static string Property(AnimationProperty value)
    {
        return Get(value switch
        {
            AnimationProperty.POSITION_X => "PositionX", AnimationProperty.POSITION_Y => "PositionY",
            AnimationProperty.SCALE_X => "ScaleX", AnimationProperty.SCALE_Y => "ScaleY", AnimationProperty.ROTATION => "Rotation",
            AnimationProperty.OPACITY => "Opacity", AnimationProperty.FILL_RED => "FillRed", AnimationProperty.FILL_GREEN => "FillGreen",
            AnimationProperty.FILL_BLUE => "FillBlue", AnimationProperty.FILL_ALPHA => "FillAlpha", AnimationProperty.STROKE_RED => "StrokeRed",
            AnimationProperty.STROKE_GREEN => "StrokeGreen", AnimationProperty.STROKE_BLUE => "StrokeBlue", AnimationProperty.STROKE_ALPHA => "StrokeAlpha",
            AnimationProperty.STROKE_WIDTH => "StrokeWidth", AnimationProperty.BLUR => "Blur", AnimationProperty.PATH_PROGRESS => "PathProgress",
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        });
    }
}
