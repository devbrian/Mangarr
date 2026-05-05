using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Manga
{
    // Service implementation for Chapter row. Mirrors Sonarr's EpisodeService
    // (Tv/EpisodeService.cs:44-329) shape, slimmed for Phase 2 deliverables:
    //   * No IHandle<EpisodeFileDeletedEvent>/EpisodeFileAddedEvent — Phase 4 territory
    //   * No IHandleAsync<SeriesScannedEvent> — no scan in Phase 2
    //   * No IConfigService dependency (no AutoUnmonitor*Episodes config in Phase 2)
    //   * No ICached<HashSet<int>> — that supports the SeriesScanned tombstone cache
    //     which we don't need until Phase 4
    public class ChapterService : IChapterService, IHandleAsync<MangaDeletedEvent>
    {
        private readonly IChapterRepository _chapterRepository;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public ChapterService(IChapterRepository chapterRepository,
                              IEventAggregator eventAggregator,
                              Logger logger)
        {
            _chapterRepository = chapterRepository;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public Chapter GetChapter(int id)
        {
            return _chapterRepository.Get(id);
        }

        public List<Chapter> GetChapters(IEnumerable<int> ids)
        {
            return _chapterRepository.Get(ids).ToList();
        }

        public Chapter FindByMangaAndNumber(int mangaId, decimal chapterNumber, string translatedLanguage)
        {
            return _chapterRepository.Find(mangaId, chapterNumber, translatedLanguage);
        }

        public List<Chapter> GetChaptersByManga(int mangaId)
        {
            return _chapterRepository.GetByMangaId(mangaId);
        }

        public List<Chapter> GetChaptersByManga(List<int> mangaIds)
        {
            // Phase 8 audit (EpisodeService-vs-ChapterService.md gap-10) — pass-through.
            // Mirrors TV's EpisodeService.GetEpisodesBySeries(List<int>) (Tv/EpisodeService.cs:103-106)
            // wrapping IEpisodeRepository.GetEpisodesBySeriesIds. Manga sibling wraps the
            // existing IChapterRepository.GetChaptersByMangaIds.
            return _chapterRepository.GetChaptersByMangaIds(mangaIds);
        }

        public List<Chapter> GetSyntheticChaptersByManga(int mangaId)
        {
            return _chapterRepository.GetSyntheticByMangaId(mangaId);
        }

        public List<Chapter> AllMissingMonitoredChapters()
        {
            // Phase 6 D-09 — pass-through to repository. Consumed by Plan 06-06
            // MissingChapterSearchService which then filters by Manga.Monitored.
            return _chapterRepository.AllMissingMonitoredChapters();
        }

        public List<Chapter> ChaptersWithFiles(int mangaId)
        {
            // Phase 8 audit (EpisodeService-vs-ChapterService.md gap-11) — pass-through.
            // Mirrors TV's EpisodeService.EpisodesWithFiles (Tv/EpisodeService.cs:156-159).
            return _chapterRepository.ChaptersWithFiles(mangaId);
        }

        public List<Chapter> GetChaptersByFileId(int fileId)
        {
            // Phase 8 audit (EpisodeService-vs-ChapterService.md gap-02) — pass-through.
            // Mirrors TV's EpisodeService.GetEpisodesByFileId (Tv/EpisodeService.cs:166-169)
            // wrapping IEpisodeRepository.GetEpisodeByFileId. Manga sibling wraps the
            // existing IChapterRepository.GetChapterByFileId.
            return _chapterRepository.GetChapterByFileId(fileId);
        }

        public PagingSpec<Chapter> ChaptersWithoutFiles(PagingSpec<Chapter> pagingSpec)
        {
            // Plan 06-09 — pass-through. The repository pre-pends a `ChapterFileId IS NULL`
            // filter onto the spec; the controller composes monitored / mangaIds / languages
            // filters on top via PagingSpec.FilterExpressions.
            return _chapterRepository.ChaptersWithoutFiles(pagingSpec);
        }

        public void UpdateChapter(Chapter chapter)
        {
            _chapterRepository.Update(chapter);
        }

        public void SetChapterMonitored(int chapterId, bool monitored)
        {
            // BL-03 mitigation. Use Find (returns null on miss) instead of Get
            // (BasicRepository.Get throws ModelNotFoundException), so the null-guard
            // below actually fires when the chapter row was cascade-deleted in the
            // MangaDeletedEvent window or when a stale UI request arrives after the
            // row was removed. (See SONARR-AUDIT.md F-02.)
            var chapter = _chapterRepository.Find(chapterId);

            if (chapter == null)
            {
                _logger.Warn("SetChapterMonitored: Chapter:{0} not found (cascade-delete window or stale request); skipping monitor update.", chapterId);
                return;
            }

            chapter.Monitored = monitored;
            _chapterRepository.Update(chapter);

            // Phase 7 Plan 07-01 — Pitfall 4 ordering invariant: DB write FIRST, event LAST.
            // ChapterUpdatedEvent drives the SignalR `chapter` push wired by ChapterController
            // (Sonarr divergence: NEW manga event sibling — see DIVERGENCE.md).
            _eventAggregator.PublishEvent(new ChapterUpdatedEvent(chapter));

            _logger.Debug("Monitored flag for Chapter:{0} was set to {1}", chapterId, monitored);
        }

        // Sonarr divergence: bulk overload added in Phase 7 Plan 07-01 per D-07 — see DIVERGENCE.md.
        // Role-match analog: EpisodeService.SetMonitored(IEnumerable<int>, bool) which delegates
        // to EpisodeRepository.SetMonitored. The manga sibling adds an explicit per-id
        // ChapterUpdatedEvent fan-out so the SignalR `chapter` resource broadcasts every
        // affected row (TV emits its updates via the EpisodeFile pipeline; manga has no
        // equivalent intermediary in v1, so the event has to be raised here).
        //
        // RESEARCH Pitfall 4 ordering invariant: the bulk DB write commits FIRST; the event
        // fan-out runs LAST so subscribers always read post-write state.
        public void SetChaptersMonitored(IEnumerable<int> chapterIds, bool monitored)
        {
            var ids = chapterIds.ToList();
            if (ids.Count == 0)
            {
                return;
            }

            // DB write FIRST — ChapterRepository.SetMonitored loads the rows, flips the flag,
            // and UpdateMany's via the BasicRepository pipeline.
            _chapterRepository.SetMonitored(ids, monitored);

            // Event fan-out LAST. Re-fetch every affected row so subscribers see the persisted
            // post-write state. Find (not Get) so a row cascade-deleted between the write
            // and the publish returns null instead of throwing ModelNotFoundException.
            // (See SONARR-AUDIT.md F-02.)
            foreach (var id in ids)
            {
                var chapter = _chapterRepository.Find(id);
                if (chapter == null)
                {
                    _logger.Warn("SetChaptersMonitored: Chapter:{0} not found post-write (cascade-delete window); skipping event publish.", id);
                    continue;
                }

                _eventAggregator.PublishEvent(new ChapterUpdatedEvent(chapter));
            }

            _logger.Debug("Monitored flag for {0} chapter(s) was set to {1}", ids.Count, monitored);
        }

        public void InsertMany(List<Chapter> chapters)
        {
            _chapterRepository.InsertMany(chapters);
        }

        public void UpdateMany(List<Chapter> chapters)
        {
            _chapterRepository.UpdateMany(chapters);
        }

        public void DeleteMany(List<Chapter> chapters)
        {
            _chapterRepository.DeleteMany(chapters);
        }

        // Phase 8 audit (EpisodeService-vs-ChapterService.md gap-05) — sibling of TV's
        // `EpisodeService.HandleAsync(SeriesDeletedEvent)` (Tv/EpisodeService.cs:308-312).
        // Bulk-deletes all chapters linked to the deleted manga. Required because
        // `001_mangarr_baseline.cs` Chapters.MangaId is declared as a plain
        // `.AsInt32().NotNullable()` column with no `.ForeignKey(...).OnDelete(Rule.Cascade)`,
        // so SQLite does NOT cascade row removal on parent delete — the service must do it.
        // Shape divergence: MangaDeletedEvent carries a single `Manga` (not `List<Series>`
        // like TV's SeriesDeletedEvent), so we wrap the id in a single-element list to
        // reuse the existing `IChapterRepository.GetChaptersByMangaIds` bulk path.
        public void HandleAsync(MangaDeletedEvent message)
        {
            var chapters = _chapterRepository.GetChaptersByMangaIds(new List<int> { message.Manga.Id });
            _chapterRepository.DeleteMany(chapters);
        }
    }
}
