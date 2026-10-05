namespace AegiNext.Desktop.Controls;

internal readonly record struct SyntaxToken(int Start, int Length, SyntaxTokenKind Kind);
