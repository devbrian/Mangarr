using System;
using System.Collections.Generic;
using System.Linq;
using NLog;

namespace NzbDrone.Core.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 8 audit
    // (no-sibling/EpisodeMonitoredService.md) — see DIVERGENCE.md.
    //
    // Mirrors Tv/EpisodeMonitoredService.cs shape: switches on a Monitor enum
    // and flips the per-Chapter Monitored flag accordingly. Diverges on the
    // enum cardinality — TV's 13-value MonitorTypes collapses to the single
    // canonical 7-value MangaMonitor (#357 D-2) because manga has no Volumes/
    // Specials/Pilot/SceneNumbering concepts to gate.
    //
    // Mapping vs TV (per #357 D-2 / D-3):
    //   * MangaMonitor.All      → all chapters monitored
    //   * MangaMonitor.Future   → ReleaseDate > UtcNow OR ReleaseDate is null
    //                             (TV's "future" semantics; null treated as
    //                              unreleased, matching Episode.AirDateUtc null path)
    //   * MangaMonitor.Missing  → chapters without a ChapterFile (ChapterFileId is null)
    //   * MangaMonitor.Existing → chapters ALREADY released (FirstReleaseDate <= UtcNow)
    //                             — the manga peer of Sonarr's MonitorTypes.Existing;
    //                             the exact inverse of the Future predicate (#357 D-3)
    //   * MangaMonitor.First    → only the lowest ChapterNumber row — the manga peer
    //                             of Sonarr's MonitorTypes.First (#357 D-3); mirrors
    //                             Latest but with Min instead of Max
    //   * MangaMonitor.Latest   → only the highest ChapterNumber row (replaces
    //                             TV's LastSeason — manga has no Volume table,
    //                             so "most recent" is per-Chapter, not per-Volume)
    //   * MangaMonitor.None     → no chapters monitored
    //
    // Existing + First DO surface now (#357 unified the import-list MonitorTypes onto
    // MangaMonitor, which gained these two values). Still drops TV-only enum values:
    // Pilot/FirstSeason/LastSeason/MonitorSpecials/UnmonitorSpecials all depend on
    // Seasons (no manga sibling); Recent depends on AirDateUtc 90-day window (subsumed
    // by Future for v1).
    //
    // Honors AddMangaOptions.IgnoreChaptersWithFiles / IgnoreChaptersWithoutFiles
    // overrides as a final pass — same precedent as TV's
    // LegacySetEpisodeMonitoredStatus path, applied unconditionally instead of
    // gated on a "v2 fallback" sentinel.
    //
    // Consumer wiring lives in MangaScannedHandler (Phase 8 Plan 03-11).
    public interface IChapterMonitoredService
    {
        void SetChapterMonitoredStatus(Manga manga, AddMangaOptions options);
    }

    public class ChapterMonitoredService : IChapterMonitoredService
    {
        private readonly IChapterService _chapterService;
        private readonly Logger _logger;

        public ChapterMonitoredService(IChapterService chapterService, Logger logger)
        {
            _chapterService = chapterService;
            _logger = logger;
        }

        public void SetChapterMonitoredStatus(Manga manga, AddMangaOptions options)
        {
            if (options == null)
            {
                return;
            }

            var chapters = _chapterService.GetChaptersByManga(manga.Id);

            if (chapters.Count == 0)
            {
                _logger.Debug("[{0}] No chapters to apply monitor policy to.", manga.Title);
                return;
            }

            switch (options.Monitor)
            {
                case MangaMonitor.All:
                    _logger.Debug("[{0}] Monitoring all chapters", manga.Title);
                    ToggleChaptersMonitoredState(chapters, c => true);
                    break;

                case MangaMonitor.Future:
                    _logger.Debug("[{0}] Monitoring future chapters", manga.Title);

                    // Sonarr divergence: Phase 16 D-02 — chapter.ReleaseDate -> chapter.FirstReleaseDate.
                    ToggleChaptersMonitoredState(chapters,
                        c => !c.FirstReleaseDate.HasValue || c.FirstReleaseDate.Value > DateTime.UtcNow);
                    break;

                case MangaMonitor.Missing:
                    _logger.Debug("[{0}] Monitoring missing chapters", manga.Title);
                    ToggleChaptersMonitoredState(chapters, c => !c.ChapterFileId.HasValue);
                    break;

                case MangaMonitor.Existing:
                    _logger.Debug("[{0}] Monitoring existing (already-released) chapters", manga.Title);

                    // #357 D-3: the exact inverse of the Future predicate — chapters whose
                    // FirstReleaseDate is set and at-or-before now are "already released".
                    ToggleChaptersMonitoredState(chapters,
                        c => c.FirstReleaseDate.HasValue && c.FirstReleaseDate.Value <= DateTime.UtcNow);
                    break;

                case MangaMonitor.First:
                    _logger.Debug("[{0}] Monitoring first chapter", manga.Title);
                    var firstNumber = chapters.Min(c => c.ChapterNumber);
                    ToggleChaptersMonitoredState(chapters, c => c.ChapterNumber == firstNumber);
                    break;

                case MangaMonitor.Latest:
                    _logger.Debug("[{0}] Monitoring latest chapter", manga.Title);
                    var latestNumber = chapters.Max(c => c.ChapterNumber);
                    ToggleChaptersMonitoredState(chapters, c => c.ChapterNumber == latestNumber);
                    break;

                case MangaMonitor.None:
                    _logger.Debug("[{0}] Unmonitoring all chapters", manga.Title);
                    ToggleChaptersMonitoredState(chapters, c => false);
                    break;
            }

            // Final-pass overrides — match TV's LegacySetEpisodeMonitoredStatus
            // ignore-with-files / ignore-without-files semantics.
            if (options.IgnoreChaptersWithFiles)
            {
                _logger.Debug("[{0}] Unmonitoring chapters with files (IgnoreChaptersWithFiles)", manga.Title);
                ToggleChaptersMonitoredState(chapters.Where(c => c.ChapterFileId.HasValue), false);
            }

            if (options.IgnoreChaptersWithoutFiles)
            {
                _logger.Debug("[{0}] Unmonitoring chapters without files (IgnoreChaptersWithoutFiles)", manga.Title);
                ToggleChaptersMonitoredState(chapters.Where(c => !c.ChapterFileId.HasValue), false);
            }

            _chapterService.UpdateMany(chapters);
        }

        private void ToggleChaptersMonitoredState(IEnumerable<Chapter> chapters, bool monitored)
        {
            foreach (var chapter in chapters)
            {
                chapter.Monitored = monitored;
            }
        }

        private void ToggleChaptersMonitoredState(List<Chapter> chapters, Func<Chapter, bool> predicate)
        {
            ToggleChaptersMonitoredState(chapters.Where(predicate), true);
            ToggleChaptersMonitoredState(chapters.Where(c => !predicate(c)), false);
        }
    }
}
