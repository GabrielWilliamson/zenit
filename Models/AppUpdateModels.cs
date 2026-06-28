namespace Zenit.Models;

public enum AppUpdateActionKind
{
    None,
    CheckForUpdates,
    ApplyPendingRestart
}

public sealed record AppUpdateStatus(
    string CurrentVersion,
    string InstallationMode,
    string StatusMessage,
    string ActionButtonText,
    bool IsActionEnabled,
    AppUpdateActionKind ActionKind);
