using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Download.Clients.InProcess
{
    /// <summary>
    /// Phase 4 — daily housekeeping for the in-process downloader.
    /// Sweeps Failed rows past <c>RetentionUntil</c> (D-08) and orphan scratch dirs (no matching DB row).
    /// Registered via <c>TaskManager.defaultTasks</c> at runtime — NOT seeded in 001_mangarr_baseline.cs
    /// (per <c>.claude/skills/sonarr-consistency-audit/SKILL.md</c> anti-pattern C; mirrors Phase 2
    /// RefreshMangaCommand registration pattern).
    /// </summary>
    public class HousekeepInProcessDownloadsCommand : Command
    {
        public override bool SendUpdatesToClient => false;
        public override bool RequiresDiskAccess => true;
    }
}
