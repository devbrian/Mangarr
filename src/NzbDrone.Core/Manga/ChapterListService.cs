using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Manga;

namespace NzbDrone.Core.Manga
{
    /// <summary>
    /// Phase 16 D-04 + STRUCT-05 INTERIM stub. Plan 16-03 splits this into
    /// <c>EnsureChapter</c> + <c>SyncChapterReleases</c> per the Sonarr-mirror two-step
    /// pattern; for the Plan 16-02 build-green-at-the-boundary contract this body has been
    /// REWRITTEN to operate on the new canonical Chapter grain (one row per
    /// (MangaId, ChapterNumber); language data lifted to ChapterRelease).
    ///
    /// Sonarr divergence: TODO(plan-16-03) — replace with the EnsureChapter +
    /// SyncChapterReleases split per STRUCT-05. Strategy 1 / 2 / 3 contracts preserved;
    /// the per-language fan-out is gone (per-translation rows are NOT inserted here in
    /// Plan 16-02; Plan 16-03 reintroduces them via SyncChapterReleases against
    /// IChapterReleaseRepository).
    /// </summary>
    public class ChapterListService : IChapterListService
    {
        private readonly IChapterRepository _chapterRepo;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public ChapterListService(IChapterRepository chapterRepo,
                                  IEventAggregator eventAggregator,
                                  Logger logger)
        {
            _chapterRepo = chapterRepo;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public void SyncChapters(Manga manga, List<Chapter> incoming)
        {
            var existing = _chapterRepo.GetByMangaId(manga.Id);

            // STRATEGY 1 (D-17.1): MangaDex linked → real-feed rows are source of truth.
            // Phase 16 collapse: incoming rows are now canonical (one per ChapterNumber).
            // Plan 16-02 stub: dedupe by ChapterNumber and insert any new canonical rows.
            // Plan 16-03 reintroduces the per-translation upsert via SyncChapterReleases.
            if (manga.MangaDexId.HasValue && incoming != null && incoming.Any())
            {
                var existingNumbers = existing.Select(e => e.ChapterNumber).ToHashSet();

                foreach (var c in incoming.GroupBy(x => x.ChapterNumber).Select(g => g.First()))
                {
                    if (existingNumbers.Contains(c.ChapterNumber))
                    {
                        continue;
                    }

                    c.MangaId = manga.Id;
                    _chapterRepo.Insert(c);
                    existingNumbers.Add(c.ChapterNumber);
                }

                _eventAggregator.PublishEvent(new ChapterListUpdatedEvent(manga));
                return;
            }

            // STRATEGY 2 (D-17.2): MangaDex NOT linked AND primary returned chapter-count > 0 → synthesize.
            // Phase 16 D-04: zero-release Chapters render as Missing — no IsSynthetic flag stored;
            // synthetic-ness now derives from "0 ChapterRelease rows".
            if (!manga.MangaDexId.HasValue && manga.TotalChapterCount.HasValue && manga.TotalChapterCount > 0)
            {
                if (existing.Any())
                {
                    _logger.Trace("ChapterList: {0} already synthesized ({1} rows); skipping resynthesis",
                        manga.Title,
                        existing.Count);
                    return;
                }

                for (var n = 1; n <= manga.TotalChapterCount.Value; n++)
                {
                    _chapterRepo.Insert(new Chapter
                    {
                        MangaId = manga.Id,
                        ChapterNumber = n,
                        Title = null,
                        ChapterType = ChapterType.Regular,
                        Monitored = true,
                    });
                }

                _eventAggregator.PublishEvent(new ChapterListUpdatedEvent(manga));
                return;
            }

            // STRATEGY 3 (D-17.3): chapter-count null → empty + warning (MissingChapterListHealthCheck fires).
            _logger.Warn("Manga {0} has no MangaDex link and no chapter-count from primary — empty chapter list",
                manga.Title);
        }
    }
}
