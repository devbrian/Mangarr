using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Manga.Events;

namespace NzbDrone.Core.HealthCheck.Checks
{
    // Manga sibling of TV `RemovedSeriesCheck` (Phase 8 cluster 13-housekeeping, audit
    // gap `single` per .planning/phases/08-tv-manga-parity-audit/audit/no-sibling/RemovedSeriesCheck.md).
    //
    // Surfaces a UI warning when manga have been marked deleted-at-source by the
    // primary metadata provider — Plan 01-10's RefreshMangaService sets
    // `Manga.Status = "deleted"` when MangaDex/AniList/MAL return 404 for the
    // canonical ID. Mirrors RemovedSeriesCheck.cs verbatim shape; diverges only on:
    //   * Series.Status (SeriesStatusType enum) → Manga.Status (string sentinel
    //     "deleted" — see Manga.cs:61 and PROJECT.md status set).
    //   * TvdbId int label → MangaDexId Guid? label (manga's primary canonical ID
    //     per Phase 2 D-09).
    //   * HealthCheckReason reuses RemovedSeriesSingle/Multiple — the enum is a
    //     reason code, not a domain label, and adding manga-specific entries would
    //     widen this commit beyond the one-file backfill scope. Parity follow-up
    //     can rename the enum members (and add localization keys) when the v1
    //     Tv/→Manga/ cutover lands.
    [CheckOn(typeof(MangaUpdatedEvent))]
    [CheckOn(typeof(MangaDeletedEvent))]
    [CheckOn(typeof(MangaRefreshCompleteEvent))]
    public class RemovedMangaCheck : HealthCheckBase, ICheckOnCondition<MangaUpdatedEvent>, ICheckOnCondition<MangaDeletedEvent>
    {
        private readonly IMangaService _mangaService;

        public RemovedMangaCheck(IMangaService mangaService, ILocalizationService localizationService)
            : base(localizationService)
        {
            _mangaService = mangaService;
        }

        public override HealthCheck Check()
        {
            var deletedManga = _mangaService.GetAllManga().Where(m => m.Status == MangaStatusType.Deleted).ToList();

            if (deletedManga.Empty())
            {
                return new HealthCheck(GetType());
            }

            var mangaText = deletedManga.Select(m => $"{m.Title} (mangadexid {m.MangaDexId})").Join(", ");

            if (deletedManga.Count == 1)
            {
                return new HealthCheck(GetType(),
                    HealthCheckResult.Error,
                    HealthCheckReason.RemovedSeriesSingle,
                    _localizationService.GetLocalizedString("RemovedMangaSingleRemovedHealthCheckMessage", new Dictionary<string, object>
                    {
                        { "series", mangaText }
                    }),
                    "#manga-removed-from-source");
            }

            return new HealthCheck(GetType(),
                HealthCheckResult.Error,
                HealthCheckReason.RemovedSeriesMultiple,
                _localizationService.GetLocalizedString("RemovedMangaMultipleRemovedHealthCheckMessage", new Dictionary<string, object>
                {
                    { "series", mangaText }
                }),
                "#manga-removed-from-source");
        }

        public bool ShouldCheckOnEvent(MangaDeletedEvent deletedEvent)
        {
            return deletedEvent.Manga.Status == MangaStatusType.Deleted;
        }

        public bool ShouldCheckOnEvent(MangaUpdatedEvent updatedEvent)
        {
            return updatedEvent.Manga.Status == MangaStatusType.Deleted;
        }
    }
}
