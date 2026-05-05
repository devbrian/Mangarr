using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Manga
{
    // Dapper repository for Chapter. Mirrors Sonarr's EpisodeRepository
    // (Tv/EpisodeRepository.cs:36-285) shape, slimmed to Phase 2 deliverables —
    // PagedQuery joins / EpisodesWithFiles / EpisodesWhereCutoffUnmet /
    // SetFileId / ClearFileId all stay in TV land (Phase 4 archiver territory).
    public class ChapterRepository : BasicRepository<Chapter>, IChapterRepository
    {
        private readonly Logger _logger;

        public ChapterRepository(IMainDatabase database, IEventAggregator eventAggregator, Logger logger)
            : base(database, eventAggregator)
        {
            _logger = logger;
        }

        public Chapter Find(int mangaId, decimal chapterNumber, string translatedLanguage)
        {
            return Query(c => c.MangaId == mangaId
                              && c.ChapterNumber == chapterNumber
                              && c.TranslatedLanguage == translatedLanguage)
                .SingleOrDefault();
        }

        public List<Chapter> GetByMangaId(int mangaId)
        {
            return Query(c => c.MangaId == mangaId).ToList();
        }

        public List<Chapter> GetSyntheticByMangaId(int mangaId)
        {
            return Query(c => c.MangaId == mangaId && c.IsSynthetic).ToList();
        }

        public List<Chapter> GetChapterByFileId(int fileId)
        {
            // Phase 8 audit (EpisodeRepository-vs-ChapterRepository.md gap-02). Mirrors TV's
            // EpisodeRepository.GetEpisodeByFileId (line 90-93). Manga's ChapterFileId is
            // nullable, so the equality predicate matches via Nullable<int>.Value semantics.
            return Query(c => c.ChapterFileId == fileId).ToList();
        }

        public List<Chapter> AllMissingMonitoredChapters()
        {
            // Monitored chapter rows with no ChapterFile imported yet.
            // Manga.Monitored filter applied at the consumer layer (Plan 06-06
            // MissingChapterSearchService) — this method intentionally returns chapters
            // for unmonitored manga so consumers can choose their own filter logic.
            return Query(c => c.Monitored && c.ChapterFileId == null).ToList();
        }

        public PagingSpec<Chapter> ChaptersWithoutFiles(PagingSpec<Chapter> pagingSpec)
        {
            // Plan 06-09 — paged variant for the V5 Wanted/Missing controller. Pre-pends a
            // "Chapter.ChapterFileId IS NULL" filter onto the spec; any caller-supplied
            // FilterExpressions (mangaIds, monitored, languages) compose on top via the
            // existing BasicRepository.AddFilters pipeline. V1 simplification: no JOIN to
            // Manga table — controller hydrates Manga via service-layer Get when needed
            // (mirrors Plan 06-03 ChapterHistoryRepository.GetPaged convention).
            pagingSpec.FilterExpressions.Add(c => c.ChapterFileId == null);
            return GetPaged(pagingSpec);
        }

        public PagingSpec<Chapter> ChaptersWhereCutoffUnmet(PagingSpec<Chapter> pagingSpec,
                                                           List<int> belowCutoffTranslationProfileIds,
                                                           List<int> belowCutoffCustomFormatProfileIds)
        {
            // Phase 8 audit (no-sibling/EpisodeCutoffService.md + gap-13). Mirrors TV's
            // EpisodeRepository.EpisodesWhereCutoffUnmet shape (line 126-141) with manga-axis swap:
            // language rank + CF score live on different entities than TV's single QualityProfile,
            // so the SQL filter narrows to chapters whose Manga is assigned to ANY profile id present
            // in the "below cutoff" lists. The DecisionEngine layer will re-evaluate per-release once
            // the consumer (Wanted/Cutoff feed) acts on the result. The audit accepts medium
            // complexity here — ChapterFile carries no stored CustomFormatScore column, so a perfect
            // SQL-level CF cutoff is impractical at this layer.
            var hasAnyTranslation = belowCutoffTranslationProfileIds != null && belowCutoffTranslationProfileIds.Any();
            var hasAnyCustomFormat = belowCutoffCustomFormatProfileIds != null && belowCutoffCustomFormatProfileIds.Any();

            if (!hasAnyTranslation && !hasAnyCustomFormat)
            {
                pagingSpec.Records = new List<Chapter>();
                pagingSpec.TotalRecords = 0;
                return pagingSpec;
            }

            pagingSpec.FilterExpressions.Add(c => c.ChapterFileId != null);
            pagingSpec.FilterExpressions.Add(c => c.Monitored);

            // Project to Manga.Id list of "candidate" mangas — the union of mangas whose
            // TranslationProfileId or CustomFormatProfileId appears in the below-cutoff sets.
            // We compute this in C# via a small Manga lookup (the candidate set is bounded by
            // library size and amortizes well — same approach as Tv/EpisodeRepository's per-call
            // qualitiesBelowCutoff projection at line 232-241).
            var translationProfileIds = belowCutoffTranslationProfileIds ?? new List<int>();
            var customFormatProfileIds = belowCutoffCustomFormatProfileIds ?? new List<int>();

            var candidateMangaIds = _database.Query<Manga>(
                    new SqlBuilder(_database.DatabaseType)
                        .Where<Manga>(m =>
                            (m.TranslationProfileId != null && translationProfileIds.Contains(m.TranslationProfileId.Value)) ||
                            (m.CustomFormatProfileId != null && customFormatProfileIds.Contains(m.CustomFormatProfileId.Value))))
                .Select(m => m.Id)
                .ToList();

            if (!candidateMangaIds.Any())
            {
                pagingSpec.Records = new List<Chapter>();
                pagingSpec.TotalRecords = 0;
                return pagingSpec;
            }

            pagingSpec.FilterExpressions.Add(c => candidateMangaIds.Contains(c.MangaId));

            return GetPaged(pagingSpec);
        }

        public void SetMonitored(IEnumerable<int> ids, bool monitored)
        {
            var chapters = Get(ids).ToList();
            foreach (var chapter in chapters)
            {
                chapter.Monitored = monitored;
            }

            UpdateMany(chapters);
        }
    }
}
