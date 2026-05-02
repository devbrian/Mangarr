using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Manga;

namespace NzbDrone.Core.Manga
{
    /// <summary>
    /// Implements D-17 chapter-list synthesis fallback. Mirrors the precedent shape from
    /// RESEARCH §Code Examples Pattern 7. Wires into Plan 02-03's <see cref="IChapterRepository"/>
    /// and publishes <see cref="ChapterListUpdatedEvent"/> after successful sync.
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
            if (manga.MangaDexId.HasValue && incoming != null && incoming.Any())
            {
                // Replace synthetic rows whose ChapterNumber matches an incoming real row.
                var byNumber = incoming
                    .GroupBy(c => c.ChapterNumber)
                    .ToDictionary(g => g.Key, g => g.First());
                foreach (var ex in existing.Where(e => e.IsSynthetic))
                {
                    if (byNumber.TryGetValue(ex.ChapterNumber, out var real))
                    {
                        real.Id = ex.Id;
                        real.MangaId = manga.Id;
                        real.IsSynthetic = false;
                        _chapterRepo.Update(real);
                        // Orphan synthetic rows kept until Phase 5 cleanup logic per D-17 trailing note.
                    }
                }

                // Insert new chapters not previously present (by (ChapterNumber, TranslatedLanguage)).
                var existingKeys = existing
                    .Select(e => (e.ChapterNumber, e.TranslatedLanguage))
                    .ToHashSet();
                foreach (var c in incoming.Where(i => !existingKeys.Contains((i.ChapterNumber, i.TranslatedLanguage))))
                {
                    c.MangaId = manga.Id;
                    c.IsSynthetic = false;
                    _chapterRepo.Insert(c);
                }

                _eventAggregator.PublishEvent(new ChapterListUpdatedEvent(manga));
                return;
            }

            // STRATEGY 2 (D-17.2): MangaDex NOT linked AND primary returned chapter-count > 0 → synthesize.
            if (!manga.MangaDexId.HasValue && manga.TotalChapterCount.HasValue && manga.TotalChapterCount > 0)
            {
                if (existing.Any())
                {
                    _logger.Trace("ChapterList: {0} already synthesized ({1} rows); skipping resynthesis",
                        manga.Title, existing.Count);
                    return;
                }

                for (int n = 1; n <= manga.TotalChapterCount.Value; n++)
                {
                    _chapterRepo.Insert(new Chapter
                    {
                        MangaId = manga.Id,
                        ChapterNumber = n,
                        Title = null,
                        ChapterType = ChapterType.Regular,
                        IsSynthetic = true,
                        // PER RESEARCH §Open Question 3: BCP-47 "und" sentinel — preserves NotNullable column.
                        TranslatedLanguage = "und",
                        ScanlationGroup = null,
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
