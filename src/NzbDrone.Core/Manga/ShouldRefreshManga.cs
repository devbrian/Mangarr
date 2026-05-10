using System;
using System.Linq;
using NLog;

namespace NzbDrone.Core.Manga
{
    // Phase 8 backfill — audit gap-03 + no-sibling/ShouldRefreshSeries.md. Manga-side
    // parity for TV ShouldRefreshSeries (Tv/ShouldRefreshSeries.cs:13-78). Same
    // 30-day / 6-hour LastInfoSync windows as TV, plus a "manga not getting new chapters"
    // skip-set on Status.
    //
    // Manga-shape adaptations vs the TV original:
    //   * Drops TV's TBA-AirDate episode peek (TV uses a Title=="TBA" + AirDateUtc<UtcNow
    //     heuristic to force-refresh series with unresolved future-aired titles; manga has
    //     no airing concept per PROJECT.md "no airing date" Out-of-Scope row).
    //   * Status skip-set: {completed, cancelled, deleted}. The "deleted" sentinel is the
    //     audit gap-07 marker (Plan 01-10 just landed) — when the primary metadata source
    //     no longer returns this manga, RefreshMangaService.Execute flips Status="deleted"
    //     and we MUST NOT re-attempt the refresh on every tick. The free-form Status
    //     string convention is documented on Manga.cs:54
    //     (ongoing | completed | hiatus | cancelled | deleted).
    //   * Recent-release heuristic: mirrors TV's "last episode aired < 30 days ago"
    //     branch (Tv/ShouldRefreshSeries.cs:61-66) using Chapter.ReleaseDate as the
    //     manga analog of Episode.AirDateUtc per CLAUDE.md mapping. Window widened to
    //     14 days because manga release cadence is weekly+, not daily — a series with
    //     a chapter in the last 2 weeks is plausibly still active even if Status says
    //     "completed" (mid-edit re-classification race).
    //   * Default: refresh (matches TV's outer try/catch fall-through behavior).
    public class ShouldRefreshManga : IShouldRefreshManga
    {
        private readonly IChapterService _chapterService;
        private readonly Logger _logger;

        public ShouldRefreshManga(IChapterService chapterService, Logger logger)
        {
            _chapterService = chapterService;
            _logger = logger;
        }

        public bool ShouldRefresh(Manga manga)
        {
            try
            {
                if (manga.LastInfoSync < DateTime.UtcNow.AddDays(-30))
                {
                    _logger.Trace("Manga {0} last updated more than 30 days ago, should refresh.", manga.Title);
                    return true;
                }

                if (manga.LastInfoSync >= DateTime.UtcNow.AddHours(-6))
                {
                    _logger.Trace("Manga {0} last updated less than 6 hours ago, should not be refreshed.", manga.Title);
                    return false;
                }

                // Status is the free-form string per Manga.cs:54. End-state set: a manga
                // that is "completed", "cancelled", or "deleted" (gap-07 sentinel) is not
                // getting new chapters and does not need to be re-pulled on every tick.
                if (manga.Status != "completed" && manga.Status != "cancelled" && manga.Status != "deleted")
                {
                    _logger.Trace("Manga {0} is not in end-state, should refresh.", manga.Title);
                    return true;
                }

                // Recent-release heuristic — mirrors TV's "last episode aired < 30 days ago"
                // (Tv/ShouldRefreshSeries.cs:61-66). Even if Status says completed, a recent
                // chapter release suggests the metadata source may still be updating (a
                // mid-edit "completed → ongoing" re-classification race, or a one-shot
                // epilogue chapter). 14-day window narrower than TV because manga moves
                // weekly+; we want to back off as soon as the trickle stops.
                // Sonarr divergence: Phase 16 D-02 — chapter.ReleaseDate -> chapter.FirstReleaseDate.
                var lastChapter = _chapterService.GetChaptersByManga(manga.Id)
                    .Where(c => c.FirstReleaseDate.HasValue)
                    .OrderByDescending(c => c.FirstReleaseDate)
                    .FirstOrDefault();

                if (lastChapter != null && lastChapter.FirstReleaseDate > DateTime.UtcNow.AddDays(-14))
                {
                    _logger.Trace("Last chapter for {0} released less than 14 days ago, should refresh.", manga.Title);
                    return true;
                }

                _logger.Trace("Manga {0} ended long ago, should not be refreshed.", manga.Title);
                return false;
            }
            catch (Exception e)
            {
                _logger.Error(e, "Unable to determine if manga should refresh, will try to refresh.");
                return true;
            }
        }
    }
}
