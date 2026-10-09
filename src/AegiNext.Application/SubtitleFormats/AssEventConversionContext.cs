using System.Collections.Immutable;
using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.SubtitleFormats;

internal sealed class AssEventConversionContext
{
    private readonly ProjectLayer layer;
    private readonly SubtitleLine line;
    private readonly ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics;
    private readonly HashSet<AnimationProperty> consumed = [];
    private readonly HashSet<string> reported = [];
    private readonly ScenePoint scale;
    private readonly double rotation;
    private readonly (double Sine, double Cosine) rotationParts;
    private readonly ScenePoint placementOffset;
    private readonly ScenePoint position;
    private readonly AssLinearMove? move;
    private readonly AssOpacityEnvelope? opacity;
    private readonly bool hasPlacement;
    private readonly bool convertedPath;
    private readonly double? letterSpacing;
    private readonly double? fillBlur;
    private readonly double? strokeBlur;

    internal AssEventConversionContext(ProjectDocument document, ProjectLayer layer, SubtitleLine line,
        ISubtitlePlacementMeasurer? measurer, ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics)
    {
        this.layer = layer;
        this.line = line;
        this.diagnostics = diagnostics;
        letterSpacing = Constant(AnimationProperty.LETTER_SPACING)?.Scalar;
        fillBlur = Constant(AnimationProperty.FILL_BLUR)?.Scalar;
        strokeBlur = Constant(AnimationProperty.STROKE_BLUR)?.Scalar;
        var opacityTrack = layer.Tracks.FirstOrDefault(track => track.Property == AnimationProperty.OPACITY);
        opacity = opacityTrack is null ? new(layer.Opacity, []) : AssOpacityConversion.FromTrack(opacityTrack);
        if (opacityTrack is not null)
        {
            if (opacity is null)
            {
                Report("Ass.OpacityAnimation", "ASS 淡入淡出不能保留这条多段或重叠透明度动画，导出时已省略该动画。");
            }
            else
            {
                consumed.Add(AnimationProperty.OPACITY);
            }
        }
        if (opacity is { Approximate: true })
        {
            Report("Ass.OpacityApproximation", "ASS 淡入淡出仅支持线性变化，已保留透明度端点、延迟和起止时间，将缓动近似为线性。");
        }
        var transform = layer.Transform;
        scale = Constant(AnimationProperty.SCALE)?.Vector ?? transform.Scale;
        rotation = Constant(AnimationProperty.ROTATION)?.Scalar ?? transform.Rotation;
        rotationParts = AssTransformMath.SinCos(rotation);
        if (scale.X <= 0 || scale.Y <= 0)
        {
            Report("Ass.TransformScale", "ASS 导出已省略非正缩放分量；项目中的镜像或折叠变换保持不变。");
            scale = new(scale.X > 0 ? scale.X : 1, scale.Y > 0 ? scale.Y : 1);
        }
        position = transform.Position;
        var positionTrack = layer.Tracks.FirstOrDefault(track => track.Property == AnimationProperty.POSITION);
        if (positionTrack is not null && AssMoveConversion.FromTrack(positionTrack) is { } converted)
        {
            consumed.Add(AnimationProperty.POSITION);
            move = converted;
            position = converted.First;
        }
        if (layer.MotionPath is { } path && (move is null || move.First == move.Last) &&
            !layer.Tracks.Any(track => track.Property == AnimationProperty.PATH_PROGRESS) &&
            AssMoveConversion.FromPath(path, position) is { } pathMove)
        {
            move = pathMove;
            position = pathMove.First;
            convertedPath = true;
        }
        if (move is { Approximate: true })
        {
            Report("Ass.MoveApproximation", "ASS move 仅支持匀速直线，已保留移动路径和起止时间，将速度变化近似为匀速。");
        }
        if (scale != new ScenePoint(1, 1))
        {
            Report("Ass.TransformLayout", "ASS 的缩放在自动换行前生效，项目在排版后缩放；长句的换行和文字边界可能不同。");
        }
        if (!ExactScale(scale.X) || !ExactScale(scale.Y))
        {
            Report("Ass.NumberPrecision", "导出的 ASS 缩放百分比保留最多 9 位小数，部分缩放数值已取近似值。");
        }
        var alignmentPivot = AssTextParser.Pivot(line.Style.Alignment);
        var stylePosition = line.Style.Position ?? SubtitlePosition.FromAlignment(line.Style.Alignment, line.Style.Margins);
        hasPlacement = line.Style.Position is not null || position != default || transform.Pivot != default ||
            scale != new ScenePoint(1, 1) || !rotation.Equals(0d) || move is not null && move.First != move.Last;
        var basis = new ScenePoint(stylePosition.Anchor.X * document.Width + stylePosition.Offset.X,
            stylePosition.Anchor.Y * document.Height + stylePosition.Offset.Y);
        var delta = new ScenePoint(-transform.Pivot.X, -transform.Pivot.Y);
        var needsMeasurement = line.Style.Position is null || stylePosition.Pivot != alignmentPivot;
        if (hasPlacement && needsMeasurement && measurer is not null)
        {
            var measuredLine = letterSpacing is { } spacing ? line with
            {
                Style = line.Style with { LetterSpacing = spacing },
                InlineSpans = line.InlineSpans.Select(span => span with
                {
                    Style = span.Style with { LetterSpacing = spacing }
                }).ToImmutableArray()
            } : line;
            var metrics = measurer.Measure(document, measuredLine);
            basis = metrics.BasePosition;
            delta = new(metrics.BoundsOrigin.X + alignmentPivot.X * metrics.BoundsSize.X - metrics.Pivot.X - transform.Pivot.X,
                metrics.BoundsOrigin.Y + alignmentPivot.Y * metrics.BoundsSize.Y - metrics.Pivot.Y - transform.Pivot.Y);
        }
        else if (hasPlacement && needsMeasurement)
        {
            Report("Ass.PlacementMeasurement", "未提供字体排版测量，定位使用九宫格边距近似；无法补偿真实字形边界和自定义文字轴心。");
        }
        var compensation = TransformVector(delta);
        placementOffset = new(basis.X + compensation.X, basis.Y + compensation.Y);
        if (layer.Tracks.Any(track => !AnimationPropertyMetadata.IsMaskProperty(track.Property) && !consumed.Contains(track.Property)) ||
            layer.MotionPath is not null && !convertedPath || layer.Blend != BlendMode.NORMAL)
        {
            Report("Subtitle.Composition", "字幕格式不能保留部分项目合成或动画；已保留可以转换的位置、缩放、旋转和透明度。");
        }
    }

    internal string GeometryTags => scale == new ScenePoint(1, 1) && rotation.Equals(0d) ? string.Empty :
        "\\fscx" + AssFormatValues.Number(scale.X * 100) + "\\fscy" + AssFormatValues.Number(scale.Y * 100) +
        "\\frz" + AssFormatValues.Number(-rotation);

    internal SubtitleStyle ApplyTypographyAnimations(SubtitleStyle style)
    {
        return style with
        {
            LetterSpacing = letterSpacing ?? style.LetterSpacing,
            FillBlur = fillBlur ?? style.FillBlur,
            StrokeBlur = strokeBlur ?? style.StrokeBlur
        };
    }

    internal SubtitleStyle ConvertStyle(SubtitleStyle style)
    {
        if (scale == new ScenePoint(1, 1) && rotation.Equals(0d))
        {
            return style;
        }
        if (!scale.X.Equals(scale.Y) && style.StrokeWidth > 0 && style.Stroke.Alpha > 0)
        {
            Report("Ass.TransformAppearance", "非等比缩放的描边已按两轴缩放的几何平均值近似，文字缩放和阴影方向仍保留。");
        }
        if (!scale.X.Equals(scale.Y) && (style.FillBlur > 0 || style.StrokeBlur > 0 || style.ShadowBlur > 0))
        {
            Report("Ass.TransformAppearance", "非等比缩放的模糊已按两轴缩放的几何平均值近似，水平与垂直扩散范围不能同时保持。");
        }
        AssExportPrecision.AddNumbers(line.Id, diagnostics, -rotation);
        return style with
        {
            StrokeWidth = style.StrokeWidth * Math.Sqrt(scale.X * scale.Y),
            FillBlur = style.FillBlur * Math.Sqrt(scale.X * scale.Y),
            StrokeBlur = style.StrokeBlur * Math.Sqrt(scale.X * scale.Y),
            ShadowBlur = style.ShadowBlur * Math.Sqrt(scale.X * scale.Y),
            ShadowOffset = TransformVector(style.ShadowOffset)
        };
    }

    internal string PlacementTags(AssMaskSample sample, MediaTime timeOffset)
    {
        var alignment = "{\\an" + AssFormatValues.Alignment(line.Style.Alignment).ToString(CultureInfo.InvariantCulture);
        if (!hasPlacement)
        {
            return alignment + "}";
        }
        var origin = new MediaTime((sample.Start + timeOffset).ToTimestamp(new(1, 100), MediaTimeRounding.FLOOR).Value, 100) -
            timeOffset - line.Start + layer.AnimationOffset;
        var end = new MediaTime((sample.End + timeOffset).ToTimestamp(new(1, 100), MediaTimeRounding.CEILING).Value, 100) -
            timeOffset - line.Start + layer.AnimationOffset;
        var startTime = move is null || move.Start < origin ? origin : move.Start;
        var endTime = move is null || move.End > end ? end : move.End;
        if (move is null || move.First == move.Last || startTime >= endTime)
        {
            var point = Place(move?.Evaluate(origin) ?? position);
            AssExportPrecision.AddNumbers(line.Id, diagnostics, point.X, point.Y);
            return alignment + "\\pos(" + Point(point) + ")}";
        }
        var first = Place(move.Evaluate(startTime));
        var last = Place(move.Evaluate(endTime));
        var startMs = Milliseconds(startTime - origin);
        var endMs = Math.Max(startMs + 1, Milliseconds(endTime - origin));
        if (new MediaTime(startMs, 1000) != startTime - origin || new MediaTime(endMs, 1000) != endTime - origin)
        {
            Report("Ass.MoveTimeQuantization", "ASS 移动时刻已取整到毫秒，极短移动至少保留 1 毫秒。");
        }
        AssExportPrecision.AddNumbers(line.Id, diagnostics, first.X, first.Y, last.X, last.Y);
        return alignment + "\\move(" + Point(first) + "," + Point(last) + "," +
            startMs.ToString(CultureInfo.InvariantCulture) + "," + endMs.ToString(CultureInfo.InvariantCulture) + ")}";
    }

    internal string OpacityTags(AssMaskSample sample, MediaTime timeOffset)
    {
        if (opacity is null)
        {
            return string.Empty;
        }
        var origin = new MediaTime((sample.Start + timeOffset).ToTimestamp(new(1, 100), MediaTimeRounding.FLOOR).Value, 100) -
            timeOffset - line.Start + layer.AnimationOffset;
        var end = new MediaTime((sample.End + timeOffset).ToTimestamp(new(1, 100), MediaTimeRounding.CEILING).Value, 100) -
            timeOffset - line.Start + layer.AnimationOffset;
        var tags = AssOpacityConversion.WriteTags(opacity, origin, end, line.Id, diagnostics);
        if (tags.Length > 0 && opacity.Clip(origin, end).HasPartialOpacity)
        {
            Report("Ass.OpacityComposition", "ASS 淡入淡出在字形组成部分绘制时应用透明度，项目在整层绘制后应用；填充、描边或阴影重叠区域可能不同。");
        }
        return tags.Length == 0 ? string.Empty : "{" + tags + "}";
    }

    private AnimationValue? Constant(AnimationProperty property)
    {
        var track = layer.Tracks.FirstOrDefault(candidate => candidate.Property == property);
        if (track is null || !AssMoveConversion.TryConstant(track, out var value))
        {
            return null;
        }
        consumed.Add(property);
        return value;
    }

    private ScenePoint Place(ScenePoint point)
    {
        return new(placementOffset.X + point.X, placementOffset.Y + point.Y);
    }

    private ScenePoint TransformVector(ScenePoint point)
    {
        var x = point.X * scale.X;
        var y = point.Y * scale.Y;
        return new(rotationParts.Cosine * x - rotationParts.Sine * y, rotationParts.Sine * x + rotationParts.Cosine * y);
    }

    private void Report(string code, string message)
    {
        if (reported.Add(code))
        {
            diagnostics.Add(new(code, message, SubtitleId: line.Id));
        }
    }

    private static long Milliseconds(MediaTime time) => time.ToTimestamp(new(1, 1000), MediaTimeRounding.TO_EVEN).Value;
    private static string Point(ScenePoint point) => AssFormatValues.Number(point.X) + "," + AssFormatValues.Number(point.Y);

    private static bool ExactScale(double value)
    {
        var serialized = AssFormatValues.Number(AssFormatValues.Number(value * 100));
        return serialized.Equals(value * 100) || (serialized / 100).Equals(value);
    }
}
