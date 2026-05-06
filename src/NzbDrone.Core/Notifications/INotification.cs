using System.Collections.Generic;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Notifications
{
    public interface INotification : IProvider
    {
        string Link { get; }

        void OnGrab(GrabMessage grabMessage);
        void OnDownload(DownloadMessage message);
        void OnRename(Series series, List<RenamedEpisodeFile> renamedFiles);
        void OnImportComplete(ImportCompleteMessage message);
        void OnEpisodeFileDelete(EpisodeDeleteMessage deleteMessage);
        void OnSeriesAdd(SeriesAddMessage message);
        void OnSeriesDelete(SeriesDeleteMessage deleteMessage);
        void OnHealthIssue(HealthCheck.HealthCheck healthCheck);
        void OnHealthRestored(HealthCheck.HealthCheck previousCheck);
        void OnApplicationUpdate(ApplicationUpdateMessage updateMessage);
        void OnManualInteractionRequired(ManualInteractionRequiredMessage message);

        // Sonarr divergence: NEW manga hook per Phase 6 D-18 + Pitfall 7 — see DIVERGENCE.md.
        // Phase 8 cleanup: collapse with OnImportComplete when domain rename runs.
        void OnChapterImport(ChapterImportMessage message);

        // Phase 8 Plan 99-08 — manga library-state hooks (siblings of OnSeriesAdd/Delete/Rename).
        // v1 default no-op (no provider overrides); v1.1+ providers (Discord / email / webhook) override.
        void OnMangaAdd(MangaAddMessage message);
        void OnMangaDelete(MangaDeleteMessage deleteMessage);
        void OnMangaRename(NzbDrone.Core.Manga.Manga manga, List<NzbDrone.Core.MediaFiles.RenamedChapterFile> renamedFiles);

        void ProcessQueue();
        bool SupportsOnGrab { get; }
        bool SupportsOnDownload { get; }
        bool SupportsOnUpgrade { get; }
        bool SupportsOnImportComplete { get; }
        bool SupportsOnRename { get; }
        bool SupportsOnSeriesAdd { get; }
        bool SupportsOnSeriesDelete { get; }
        bool SupportsOnEpisodeFileDelete { get; }
        bool SupportsOnEpisodeFileDeleteForUpgrade { get; }
        bool SupportsOnHealthIssue { get; }
        bool SupportsOnHealthRestored { get; }
        bool SupportsOnApplicationUpdate { get; }
        bool SupportsOnManualInteractionRequired { get; }
        bool SupportsOnChapterImport { get; }
        bool SupportsOnMangaAdd { get; }
        bool SupportsOnMangaDelete { get; }
        bool SupportsOnMangaRename { get; }
    }
}
