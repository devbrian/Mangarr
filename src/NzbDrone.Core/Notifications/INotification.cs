using System.Collections.Generic;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Notifications
{
    // Sonarr divergence: Phase 15 W-1 (CONTRACTS-AUDIT  Wave A Cluster 2) - TV-only hooks removed:
    // OnGrab, OnDownload, OnRename, OnEpisodeFileDelete, OnSeriesAdd, OnSeriesDelete,
    // OnImportComplete, OnManualInteractionRequired. Manga preserves OnChapterImport,
    // OnMangaAdd/Delete/Rename, OnHealthIssue/Restored, OnApplicationUpdate.
    // Atomic with notifications-extra MOVE in Plan 15-04 (the 25 TV providers that overrode
    // these hooks were already moved to .planning/reference/sonarr-vertical-slices/notifications-extra/).
    public interface INotification : IProvider
    {
        string Link { get; }

        void OnHealthIssue(HealthCheck.HealthCheck healthCheck);
        void OnHealthRestored(HealthCheck.HealthCheck previousCheck);
        void OnApplicationUpdate(ApplicationUpdateMessage updateMessage);

        // Sonarr divergence: NEW manga hook per Phase 6 D-18 + Pitfall 7 - see DIVERGENCE.md.
        void OnChapterImport(ChapterImportMessage message);

        // Phase 8 Plan 99-08 - manga library-state hooks (siblings of OnSeriesAdd/Delete/Rename).
        // v1 default no-op (no provider overrides); v1.1+ providers (Discord / email / webhook) override.
        void OnMangaAdd(MangaAddMessage message);
        void OnMangaDelete(MangaDeleteMessage deleteMessage);
        void OnMangaRename(NzbDrone.Core.Manga.Manga manga, List<NzbDrone.Core.MediaFiles.RenamedChapterFile> renamedFiles);

        // Manga siblings of TV-deleted OnEpisodeFileDelete / OnEpisodeFileDeleteForUpgrade hooks.
        // Surface-only (no v1 publisher); v1.1+ providers override. Mirrors the OnMangaAdd/Delete/Rename
        // precedent — surface added now so v1.1+ providers can opt in without churning the contract.
        void OnChapterFileDelete(ChapterFileDeleteMessage deleteMessage);
        void OnChapterFileDeleteForUpgrade(ChapterFileDeleteMessage deleteMessage);

        void ProcessQueue();
        bool SupportsOnHealthIssue { get; }
        bool SupportsOnHealthRestored { get; }
        bool SupportsOnApplicationUpdate { get; }
        bool SupportsOnChapterImport { get; }
        bool SupportsOnMangaAdd { get; }
        bool SupportsOnMangaDelete { get; }
        bool SupportsOnMangaRename { get; }
        bool SupportsOnChapterFileDelete { get; }
        bool SupportsOnChapterFileDeleteForUpgrade { get; }
    }
}
