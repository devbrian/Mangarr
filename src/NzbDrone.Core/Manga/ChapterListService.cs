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
                // BL-05 FIX: previous code grouped incoming by ChapterNumber alone and
                // kept only `g.First()` — silently dropping every other translation /
                // scanlation-group entry at the same number. MangaDex's /manga/{id}/feed
                // returns one entry per (chapter, language, group) triple, so a single
                // chapter typically has 5-20 incoming rows. Grouping by
                // (ChapterNumber, TranslatedLanguage) preserves multilingual entries.
                //
                // Synthetic-row upgrade contract: synthetic rows carry "und" and
                // represent the "chapter N exists" slot. We upgrade each synthetic IN
                // PLACE using ANY incoming row that matches its ChapterNumber (the first
                // one wins — typically English when present, else whatever the source
                // returned first). The remaining incoming rows for the same number get
                // INSERTED below as new translation rows. Without this fix the synthetic
                // stayed pinned at "und" forever (since its (N,"und") key never matched
                // any incoming (N,"en") key).
                var existingKeys = existing
                    .Select(e => (e.ChapterNumber, e.TranslatedLanguage))
                    .ToHashSet();
                var consumedIncoming = new HashSet<Chapter>();

                var incomingByNumber = incoming
                    .GroupBy(c => c.ChapterNumber)
                    .ToDictionary(g => g.Key, g => g.ToList());

                foreach (var ex in existing.Where(e => e.IsSynthetic))
                {
                    if (incomingByNumber.TryGetValue(ex.ChapterNumber, out var realList) && realList.Count > 0)
                    {
                        var real = realList[0];
                        real.Id = ex.Id;
                        real.MangaId = manga.Id;
                        real.IsSynthetic = false;
                        _chapterRepo.Update(real);
                        consumedIncoming.Add(real);

                        // Refresh existingKeys so the bulk-insert pass below does not
                        // try to re-insert the row we just upgraded in place.
                        existingKeys.Add((real.ChapterNumber, real.TranslatedLanguage));

                        // Orphan synthetic rows kept until Phase 5 cleanup logic per D-17 trailing note.
                    }
                }

                // Insert all remaining incoming chapters that are not already represented
                // by an (existing OR just-upgraded) row at the same
                // (ChapterNumber, TranslatedLanguage) key. This preserves every language
                // / group variant the source returned.
                foreach (var c in incoming)
                {
                    if (consumedIncoming.Contains(c))
                    {
                        continue;
                    }

                    if (existingKeys.Contains((c.ChapterNumber, c.TranslatedLanguage)))
                    {
                        continue;
                    }

                    c.MangaId = manga.Id;
                    c.IsSynthetic = false;
                    _chapterRepo.Insert(c);
                    existingKeys.Add((c.ChapterNumber, c.TranslatedLanguage));
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
