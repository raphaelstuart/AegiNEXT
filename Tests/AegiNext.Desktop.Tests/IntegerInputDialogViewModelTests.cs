using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Views;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests;

public sealed class IntegerInputDialogViewModelTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("125", 125)]
    [InlineData("+125", 125)]
    [InlineData("-125", -125)]
    [InlineData("2147483647", int.MaxValue)]
    [InlineData("-2147483648", int.MinValue)]
    public void SignedIntegersConfirmOnceWithoutChangingTheDraft(string text, int expected)
    {
        using var model = new IntegerInputDialogViewModel(new("Workbench.Move", "Workbench.MoveMilliseconds", "Workbench.MoveMillisecondsHint"));
        var confirmations = 0;
        model.Confirmed += (_, _) => confirmations++;
        Assert.Equal("0", model.RawText);
        model.RawText = text;

        Assert.True(model.TryConfirm());
        Assert.Equal(expected, model.Result);
        Assert.Equal(text, model.RawText);
        Assert.False(model.HasError);
        Assert.False(model.TryConfirm());
        Assert.Equal(1, confirmations);
        Assert.False(model.ConfirmCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("+")]
    [InlineData("1.5")]
    [InlineData("1e3")]
    [InlineData("1,000")]
    [InlineData("2147483648")]
    [InlineData("-2147483649")]
    [InlineData("字幕")]
    public void InvalidDraftRemainsEditableAndReportsTheIntegerRange(string text)
    {
        using var model = new IntegerInputDialogViewModel(new("Workbench.Move", "Workbench.MoveMilliseconds", "Workbench.MoveMillisecondsHint"));
        model.RawText = text;

        Assert.False(model.TryConfirm());
        Assert.Null(model.Result);
        Assert.Equal(text, model.RawText);
        Assert.True(model.HasError);
        Assert.Equal(Localization.Format("Workbench.IntegerInputInvalid", int.MinValue, int.MaxValue), model.Error);
        Assert.True(model.ConfirmCommand.CanExecute(null));
        model.RawText = "-25";
        Assert.False(model.HasError);
        Assert.True(model.TryConfirm());
        Assert.Equal(-25, model.Result);
    }

    [Theory]
    [InlineData("-10", true)]
    [InlineData("10", true)]
    [InlineData("-11", false)]
    [InlineData("11", false)]
    public void RequestedBoundsAreInclusiveAndApplyToConfirmation(string text, bool expected)
    {
        using var model = new IntegerInputDialogViewModel(new("Workbench.Move", "Workbench.MoveMilliseconds", "Workbench.MoveMillisecondsHint", 3, -10, 10));
        Assert.Equal("3", model.RawText);
        model.RawText = text;

        Assert.Equal(expected, model.TryConfirm());
        Assert.Equal(!expected, model.HasError);
        Assert.Equal(text, model.RawText);
        if (!expected)
        {
            Assert.Equal(Localization.Format("Workbench.IntegerInputInvalid", -10, 10), model.Error);
        }
    }

    [Theory]
    [InlineData(0, 10, -10)]
    [InlineData(-11, -10, 10)]
    [InlineData(11, -10, 10)]
    public void InvalidRequestIsRejectedBeforeSubscribing(int initial, int minimum, int maximum)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IntegerInputDialogViewModel(
            new("Workbench.Move", "Workbench.MoveMilliseconds", "Workbench.MoveMillisecondsHint", initial, minimum, maximum)));
    }

    [Fact]
    public void DisposalPreventsConfirmationWithoutChangingTheDraft()
    {
        var model = new IntegerInputDialogViewModel(new("Workbench.Move", "Workbench.MoveMilliseconds", "Workbench.MoveMillisecondsHint"));
        model.RawText = "125";
        model.Dispose();
        model.Dispose();

        Assert.False(model.TryConfirm());
        Assert.Null(model.Result);
        Assert.Equal("125", model.RawText);
        Assert.False(model.ConfirmCommand.CanExecute(null));
    }
}
