using AegiNext.Desktop.Controls;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using System.ComponentModel;

namespace AegiNext.Desktop.Panels.Effects;

internal sealed partial class EffectPropertyRowView : UserControl
{
    private readonly NumericDraftInput scalar;
    private readonly ColorDraftInput color;
    private readonly VectorDraftInput vector;
    private readonly Grid grid;
    private readonly StackPanel values;
    private readonly StackPanel actions;
    private EffectPropertyRowViewModel? observedRow;

    public EffectPropertyRowView()
    {
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
        scalar = this.FindControl<NumericDraftInput>("ScalarInput")!;
        color = this.FindControl<ColorDraftInput>("ColorInput")!;
        vector = this.FindControl<VectorDraftInput>("VectorInput")!;
        grid = this.FindControl<Grid>("RowGrid")!;
        values = this.FindControl<StackPanel>("ValueEditor")!;
        actions = this.FindControl<StackPanel>("RowActions")!;
        DataContextChanged += (_, _) => ObserveRow(this.IsAttachedToVisualTree());
        AttachedToVisualTree += (_, _) => ObserveRow(true);
        DetachedFromVisualTree += (_, _) => ObserveRow(false);
        SizeChanged += (_, _) =>
        {
            var narrow = Bounds.Width < 350 || DataContext is EffectPropertyRowViewModel { IsVector: true } && Bounds.Width < 520;
            grid.ColumnDefinitions = new(narrow ? "*,Auto" : "108,*,Auto");
            Grid.SetColumn(values, narrow ? 0 : 1);
            Grid.SetRow(values, narrow ? 1 : 0);
            Grid.SetColumnSpan(values, narrow ? 2 : 1);
            Grid.SetColumn(actions, narrow ? 1 : 2);
        };
    }

    private void ObserveRow(bool attached)
    {
        if (observedRow is not null)
        {
            observedRow.PropertyChanged -= OnRowChanged;
        }
        observedRow = attached ? DataContext as EffectPropertyRowViewModel : null;
        if (observedRow is not null)
        {
            observedRow.PropertyChanged += OnRowChanged;
        }
        RefreshErrors();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EffectPropertyRowViewModel.Error) or nameof(EffectPropertyRowViewModel.InvalidFieldKey))
        {
            RefreshErrors();
        }
    }

    private void RefreshErrors()
    {
        var row = DataContext as EffectPropertyRowViewModel;
        var xError = row?.InvalidFieldKey == row?.XFieldKey ? row?.Error : null;
        var yError = row?.InvalidFieldKey == row?.YFieldKey ? row?.Error : null;
        DataValidationErrors.SetErrors(scalar, xError is null ? null : new[] { xError });
        vector.SetFieldError("RangeValueXInput", xError);
        vector.SetFieldError("RangeValueYInput", yError);
    }

    internal bool OwnsField(string? field) => DataContext is EffectPropertyRowViewModel row &&
        (field == row.FieldKey || field == row.XFieldKey || field == row.YFieldKey);

    internal bool FocusField(string? field)
    {
        if (DataContext is not EffectPropertyRowViewModel row || !OwnsField(field))
        {
            return false;
        }
        if (row.IsColor)
        {
            return color.TryFocusInvalidField();
        }
        return row.IsVector ? vector.FocusField(field == row.YFieldKey ? "RangeValueYInput" : "RangeValueXInput") : scalar.FocusInput();
    }

    internal bool RestoreInput(Control source)
    {
        if (DataContext is not EffectPropertyRowViewModel row)
        {
            return false;
        }
        var input = source.GetSelfAndVisualAncestors().OfType<NumericDraftInput>().FirstOrDefault();
        if (input is null)
        {
            return false;
        }
        row.Restore(input.Name == "RangeValueYInput" ? row.YFieldKey : row.XFieldKey);
        return true;
    }
}
