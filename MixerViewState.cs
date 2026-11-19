namespace Veyro;

// Only transient navigation is sent to the tray. Preferences stay in the user's settings file.
internal sealed record MixerViewState(string[] ExpandedGroups, bool SettingsOpen, string NewGroupName,
    string? FolderKey, string? FolderName, string FolderDraft, double SettingsOffset);
