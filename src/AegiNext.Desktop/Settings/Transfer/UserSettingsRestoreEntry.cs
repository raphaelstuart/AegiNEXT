namespace AegiNext.Desktop.Settings.Transfer;

internal sealed record UserSettingsRestoreEntry(string Name, bool Existed, int Length, string? Sha256);
