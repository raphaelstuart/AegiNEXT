using System.ComponentModel;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AegiNext.Desktop.Settings.Styles;

/// <summary>样式页面局部字体、对齐确认和焦点适配。</summary>
public sealed partial class StyleSettingsView : UserControl
{
    private StyleSettingsViewModel? model;
    private bool synchronizingSelection;

    /// <summary>先隔离父级上下文，再加载编译绑定和本地控件的语义提交。</summary>
    public StyleSettingsView()
    {
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) => ChangeModel();
        this.FindControl<ListBox>("StyleList")!.SelectionChanged += SelectionChanged;
        this.FindControl<FontFamilyPicker>("FontInput")!.FamilyCommitted +=
            (_, value) => model?.CommitFont(value.Selection);
        this.FindControl<SubtitleAlignmentPicker>("AlignmentPicker")!.AlignmentCommitted +=
            (_, value) => model?.CommitAlignment(value.Alignment);
    }

    private void ChangeModel()
    {
        if (model is not null)
        {
            model.PropertyChanged -= ModelChanged;
            model.CommitPendingInputs = null;
            model.HasPendingInputs = null;
        }

        model = DataContext as StyleSettingsViewModel;
        if (model is not null)
        {
            model.PropertyChanged += ModelChanged;
            model.CommitPendingInputs = CommitFontInput;
            model.HasPendingInputs = () =>
            {
                var picker = this.FindControl<FontFamilyPicker>("FontInput")!;
                return picker.Text != picker.CurrentFont.DisplayName;
            };
            SynchronizeSelection();
            RefreshFontFamilies();
            LoadFont();
        }
    }

    private async void SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (synchronizingSelection || model is null)
        {
            return;
        }
        var list = this.FindControl<ListBox>("StyleList")!;
        var ids = list.Selection.SelectedItems.OfType<AegiNext.Core.Presets.SubtitleStylePreset>()
            .Select(value => value.Id).ToArray();
        var primary = e.AddedItems.OfType<AegiNext.Core.Presets.SubtitleStylePreset>().LastOrDefault()?.Id ??
            (model.SelectedStyle is { } current && ids.Contains(current.Id) ? current.Id : ids.Cast<Guid?>().FirstOrDefault());
        var currentModel = model;
        SynchronizeSelection();
        try
        {
            await currentModel.SelectStylesAsync(primary, ids);
        }
        finally
        {
            SynchronizeSelection();
        }
    }

    private void SynchronizeSelection()
    {
        if (model is null || synchronizingSelection)
        {
            return;
        }
        var list = this.FindControl<ListBox>("StyleList")!;
        var values = list.Items.OfType<AegiNext.Core.Presets.SubtitleStylePreset>().ToArray();
        if (list.Selection.SelectedItems.OfType<AegiNext.Core.Presets.SubtitleStylePreset>().Select(value => value.Id)
            .ToHashSet().SetEquals(model.SelectedIds))
        {
            return;
        }
        synchronizingSelection = true;
        try
        {
            using var update = list.Selection.BatchUpdate();
            list.Selection.Clear();
            var primary = Array.FindIndex(values, value => value.Id == model.SelectedStyle?.Id);
            if (primary >= 0 && model.SelectedIds.Contains(values[primary].Id))
            {
                list.Selection.Select(primary);
            }
            for (var index = 0; index < values.Length; index++)
            {
                if (index != primary && model.SelectedIds.Contains(values[index].Id))
                {
                    list.Selection.Select(index);
                }
            }
        }
        finally
        {
            synchronizingSelection = false;
        }
    }

    private bool CommitFontInput()
    {
        var picker = this.FindControl<FontFamilyPicker>("FontInput")!;
        if (picker.CommitText())
        {
            return true;
        }
        model?.RejectFont();
        picker.Focus();
        return false;
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StyleSettingsViewModel.SelectedIds) or nameof(StyleSettingsViewModel.Styles))
        {
            SynchronizeSelection();
        }
        if (e.PropertyName is nameof(StyleSettingsViewModel.Styles) or nameof(StyleSettingsViewModel.Fonts))
        {
            RefreshFontFamilies();
        }
        else if (e.PropertyName == nameof(StyleSettingsViewModel.DraftVersion))
        {
            LoadFont();
        }
        else if (e.PropertyName == nameof(StyleSettingsViewModel.AlignmentIndex))
        {
            this.FindControl<SubtitleAlignmentPicker>("AlignmentPicker")!.SetCurrentValue(
                SubtitleAlignmentPicker.AlignmentIndexProperty, model!.AlignmentIndex);
        }
    }

    private void RefreshFontFamilies()
    {
        this.FindControl<FontFamilyPicker>("FontInput")!.RefreshFontCandidates(
            model!.Fonts.Candidates, model.Styles.Select(value => value.Style.FontFamily));
    }

    private void LoadFont()
    {
        var preset = model!.Draft;
        var style = preset?.Style ?? new();
        this.FindControl<FontFamilyPicker>("FontInput")!.SetCurrentFont(
            new(style.FontFamily, style.FontVariant, preset?.Font is null && !style.FontAssetId.HasValue));
    }

    private void FocusName(object? sender, RoutedEventArgs e)
    {
        this.FindControl<TextBox>("StyleNameInput")!.Focus();
    }

    private void SaveStyle(object? sender, RoutedEventArgs e)
    {
        Submit(false);
    }

    private void ApplyStyle(object? sender, RoutedEventArgs e)
    {
        Submit(true);
    }

    private void Submit(bool apply)
    {
        if (model is null)
        {
            return;
        }

        var picker = this.FindControl<FontFamilyPicker>("FontInput")!;
        if (!picker.CommitText())
        {
            model.RejectFont();
            picker.Focus();
            return;
        }

        var command = apply ? model.ApplyCommand : model.SaveCommand;
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }

        if (model.Error is not null && model.InvalidFieldKey is { } fieldKey)
        {
            var colorField = fieldKey.Split('.', 2);
            if (colorField.Length == 2 && this.FindControl<ColorDraftInput>(colorField[0]) is { } colorInput)
            {
                _ = colorInput.TryFocusInvalidField(colorField[1]);
                return;
            }
            if (!this.FindControl<SubtitlePositionEditor>("PositionEditor")!.FocusInvalidField(fieldKey))
            {
                this.FindControl<Control>(fieldKey)!.Focus();
            }
        }
    }
}
