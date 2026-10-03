using System.ComponentModel;
using AegiNext.Desktop.Controls;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AegiNext.Desktop.Settings.Styles;

/// <summary>样式页面局部字体确认和焦点适配。</summary>
public sealed partial class StyleSettingsView : UserControl
{
    private StyleSettingsViewModel? model;

    /// <summary>创建编译绑定页面；本地控件仅提交字体语义值。</summary>
    public StyleSettingsView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) => ChangeModel();
        this.FindControl<FontFamilyPicker>("FontInput")!.FamilyCommitted +=
            (_, value) => model?.CommitFont(value.FamilyName);
    }

    private void ChangeModel()
    {
        if (model is not null)
        {
            model.PropertyChanged -= ModelChanged;
        }

        model = DataContext as StyleSettingsViewModel;
        if (model is not null)
        {
            model.PropertyChanged += ModelChanged;
            RefreshFontFamilies();
            LoadFont();
        }
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StyleSettingsViewModel.Styles))
        {
            RefreshFontFamilies();
        }
        else if (e.PropertyName == nameof(StyleSettingsViewModel.DraftVersion))
        {
            LoadFont();
        }
    }

    private void RefreshFontFamilies()
    {
        this.FindControl<FontFamilyPicker>("FontInput")!.RefreshFontFamilies(
            model!.Styles.Select(value => value.Style.FontFamily));
    }

    private void LoadFont()
    {
        this.FindControl<FontFamilyPicker>("FontInput")!.SetCurrentFamily(
            model!.Draft?.Style.FontFamily ?? "sans-serif");
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
            this.FindControl<Control>(fieldKey)!.Focus();
        }
    }
}
