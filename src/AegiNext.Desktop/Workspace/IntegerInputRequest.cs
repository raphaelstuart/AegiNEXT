namespace AegiNext.Desktop.Workspace;

internal sealed record IntegerInputRequest(string TitleKey, string LabelKey, string HintKey,
    int InitialValue = 0, int Minimum = int.MinValue, int Maximum = int.MaxValue);
