using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Manga
{
    // Dapper repository for Chapter. Mirrors Mangarr's EpisodeRepository
    // (Tv/EpisodeRepository.cs:36-285) shape, slimmed to Phase 2 deliverables —
    // PagedQuery joins / EpisodesWithFiles / EpisodesWhereCutoffUnmet stay in TV land
    // (Phase 4 archiver territory). SetFileId/ClearFileId added in Phase 8 audit (gap-03).
    public class ChapterRepository : BasicRepository<Chapter>, IChapterRepository
    {
        private readonly Logger _logger;

        public ChapterRepository(IMainDatabase database, IEventAggregator eventAggregator, Logger logger)
            : base(database, eventAggregator)
        {
            _logger = logger;
        }

        // Sonarr divergence: Phase 16 STRUCT-01 — 3-arg Find collapsed to 2-arg.
        // Language axis dropped from the canonical Chapter grain (per-translation data
        // lives on ChapterFile post-import per Phase 16.1 D-04 — Sonarr-canonical
        // pattern). Canonical Chapter is now unique on (MangaId, ChapterNumber) at the
        // SQL layer.
        public Chapter Find(int mangaId, decimal chapterNumber)
        {
            return Query(c => c.MangaId == mangaId
                              && c.ChapterNumber == chapterNumber)
                .SingleOrDefault();
        }

        public List<Chapter> GetByMangaId(int mangaId)
        {
            return Query(c => c.MangaId == mangaId).ToList();
        }

        public List<Chapter> GetChaptersByMangaIds(List<int> mangaIds)
        {
            // Phase 8 audit (EpisodeRepository-vs-ChapterRepository.md gap-01). Mirrors TV's
            // EpisodeRepository.GetEpisodesBySeriesIds (line 80-83). Bulk get-by-multiple-parent-IDs
            // for batch operations across multiple mangas.
            return Query(c => mangaIds.Contains(c.MangaId)).ToList();
        }

        public List<Chapter> GetChapterByFileId(int fileId)
        {
            // Phase 8 audit (EpisodeRepository-vs-ChapterRepository.md gap-02). Mirrors TV's
            // EpisodeRepository.GetEpisodeByFileId (line 90-93). Manga's ChapterFileId is
            // nullable, so the equality predicate matches via Nullable<int>.Value semantics.
            return Query(c => c.ChapterFileId == fileId).ToList();
        }

        public List<Chapter> ChaptersWithFiles(int mangaId)
        {
            // Phase 8 audit (EpisodeService-vs-ChapterService.md gap-11). Mirrors TV's
            // EpisodeRepository.EpisodesWithFiles (line 95-...). V1 simplification: no JOIN to
            // ChapterFile table — manga's ChapterFileId is nullable, so the `!= null` predicate
            // alone narrows to imported rows. Consumers that need the file row can hydrate via
            // ChapterFile lazy-load or a separate IChapterFileService lookup.
            return Query(c => c.MangaId == mangaId && c.ChapterFileId != null).ToList();
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

            // gh #153 fix-forward: WhereBuilder requires a CONCRETE comparison in each
            // FilterExpression; a bare member-access (e.g. `c.Monitored`) emits
            // `"Chapters"."Monitored"` with no comparator and trips
            // WhereBuilderSqlite.cs:391-395 ("WhereBuilder requires a concrete condition").
            // Pin the comparator explicitly (`== true`) so SqlBuilder always sees a binary
            // expression with a constant on the right.
            pagingSpec.FilterExpressions.Add(c => c.ChapterFileId != null);
            pagingSpec.FilterExpressions.Add(c => c.Monitored == true);

            // Project to Manga.Id list of "candidate" mangas — the union of mangas whose
            // TranslationProfileId or CustomFormatProfileId appears in the below-cutoff sets.
            // We compute this in C# via a small Manga lookup (the candidate set is bounded by
            // library size and amortizes well — same approach as Tv/EpisodeRepository's per-call
            // qualitiesBelowCutoff projection at line 232-241).
            // gh #153 fix-forward: the SqlBuilder's WhereBuilderSqlite/Postgres
            // VisitMemberAccess cannot translate `.Value` on a `Nullable<int>` —
            // it emits `NULL` in place of the column reference, so
            // `translationProfileIds.Contains(m.TranslationProfileId.Value)` projects
            // to `IN (NULL)` and never matches any row. Casting the below-cutoff
            // id list to `List<int?>` lets us drop the `.Value` and call Contains
            // directly on the nullable column, which the WhereBuilder DOES
            // translate to the correct `"Manga"."TranslationProfileId" IN (...)`
            // SQL. This unblocks the only call site of
            // ChaptersWhereCutoffUnmet (the /api/v5/manga/wanted/cutoff endpoint);
            // until this fix landed, the endpoint returned 0 rows for every
            // input, masking a real test-coverage gap that gh #153 closes.
            var translationProfileIds = (belowCutoffTranslationProfileIds ?? new List<int>())
                .Select(id => (int?)id).ToList();
            var customFormatProfileIds = (belowCutoffCustomFormatProfileIds ?? new List<int>())
                .Select(id => (int?)id).ToList();

            var candidateMangaIds = _database.Query<Manga>(
                    new SqlBuilder(_database.DatabaseType)
                        .Where<Manga>(m =>
                            (m.TranslationProfileId != null && translationProfileIds.Contains(m.TranslationProfileId)) ||
                            (m.CustomFormatProfileId != null && customFormatProfileIds.Contains(m.CustomFormatProfileId))))
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

        public void SetFileId(Chapter chapter, int fileId)
        {
            // Phase 8 audit (EpisodeRepository-vs-ChapterRepository.md gap-03). Mirrors TV's
            // EpisodeRepository.SetFileId (line 195-202). Encapsulates the field mutation +
            // SetFields + ModelUpdated publish so the Phase 4 archive + Phase 6 import sites
            // (PIPELINE-04) cannot drift on the event.
            chapter.ChapterFileId = fileId;

            SetFields(chapter, c => c.ChapterFileId);

            ModelUpdated(chapter, true);
        }

        public void ClearFileId(Chapter chapter, bool unmonitor)
        {
            // Phase 8 audit (EpisodeRepository-vs-ChapterRepository.md gap-03). Mirrors TV's
            // EpisodeRepository.ClearFileId (line 204-212). Manga's ChapterFileId is nullable
            // (vs. TV's int sentinel `0`), so we null it instead of zeroing. The unmonitor flag
            // mirrors TV's "delete-and-unmonitor" import-failure flow.
            chapter.ChapterFileId = null;
            chapter.Monitored &= !unmonitor;

            SetFields(chapter, c => c.ChapterFileId, c => c.Monitored);

            ModelUpdated(chapter, true);
        }
    }
}
