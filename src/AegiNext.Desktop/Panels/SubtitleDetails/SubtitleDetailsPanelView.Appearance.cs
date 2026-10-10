using AegiNext.Core.Projects;
using AegiNext.Desktop.Styling;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Material.Icons.Avalonia;

namespace AegiNext.Desktop.Panels.SubtitleDetails;

internal sealed partial class SubtitleDetailsPanelView
{
    private static Grid BuildVisualStateIcon(SubtitleVisualStateChoice choice, bool includeDropDown)
    {
        var icon = WorkbenchIcon.Create("HighlightStyle");
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.Margin = new(1, 0, 0, 0);
        var (badgeKey, colorResource) = choice.State switch
        {
            KaraokeVisualState.INACTIVE => ("AppearanceInactive", "WorkbenchAppearanceInactive"),
            KaraokeVisualState.ACTIVE => ("AppearanceActive", "WorkbenchAppearanceActive"),
            _ => ("AppearanceNormal", "PreviewMuted")
        };
        var badgeIcon = WorkbenchIcon.Create(badgeKey, 10);
        badgeIcon.Name = "SubtitleVisualStateBadge";
        badgeIcon.Bind(MaterialIcon.ForegroundProperty, new DynamicResourceExtension(colorResource));
        var badge = new Border
        {
            Width = 12, Height = 12, CornerRadius = new(6), BorderThickness = new(1),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Child = badgeIcon
        };
        badge.Bind(Border.BackgroundProperty, new DynamicResourceExtension("PreviewSurface"));
        badge.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("PreviewSurface"));
        var content = new Grid { Width = 24, Height = 24, IsHitTestVisible = false, Children = { icon, badge } };
        if (includeDropDown)
        {
            var arrow = WorkbenchIcon.Create("Down", 8);
            arrow.HorizontalAlignment = HorizontalAlignment.Right;
            arrow.VerticalAlignment = VerticalAlignment.Top;
            content.Children.Add(arrow);
        }
        AutomationProperties.SetAccessibilityView(content, AccessibilityView.Raw);
        return content;
    }

    private static StackPanel BuildVisualStateMenuItem(SubtitleVisualStateChoice choice)
    {
        return new()
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children =
            {
                BuildVisualStateIcon(choice, false),
                new TextBlock { Text = choice.Name, VerticalAlignment = VerticalAlignment.Center }
            }
        };
    }
}
