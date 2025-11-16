using System;

namespace PullRequestForRevit.Services;

/// <summary>
/// Global guard to control whether Revit is allowed to synchronize
/// with central. Comparison can require a user confirmation step in
/// the web viewer before sync is allowed.
/// </summary>
public static class SyncGuard
{
    private static bool _syncAllowed = true;

    /// <summary>
    /// Returns true if synchronization with central is currently allowed.
    /// </summary>
    public static bool CanSync => _syncAllowed;

    /// <summary>
    /// Require user confirmation before allowing sync. Call this after
    /// a new comparison run with unconfirmed changes.
    /// </summary>
    public static void RequireConfirmation()
    {
        _syncAllowed = false;
        Logger.Instance.LogInfo("SyncGuard: sync now requires user confirmation.");
    }

    /// <summary>
    /// Mark all current changes as confirmed and allow sync.
    /// </summary>
    public static void ConfirmAll()
    {
        _syncAllowed = true;
        Logger.Instance.LogInfo("SyncGuard: all changes confirmed, sync allowed.");
    }
}


