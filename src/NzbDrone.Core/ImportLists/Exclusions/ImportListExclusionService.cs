using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.ImportLists.Exclusions
{
    // Phase 26 Plan 26-04 (IL-06 + D-12) — ported from
    // .planning/reference/sonarr-vertical-slices/import-lists/Exclusions/ImportListExclusionService.cs
    // with critical swap per RESEARCH §Q2: `IHandleAsync<SeriesDeletedEvent>` →
    // `IHandle<MangaDeletedEvent>` (sync; matches the majority pattern of in-tree
    // MangaDeletedEvent handlers — IHandle on MangaBlocklistService / PendingRelease /
    // ChapterHistory / MangaController).
    //
    // D-12 ENFORCED: ImportListExclusionService is event-driven ONLY. NO synchronous
    // call from MangaController.Delete — the V5 controller publishes MangaDeletedEvent
    // and this handler does the auto-add atomically.
    //
    // FindByMangaDexId guard prevents double-insert when MangaService.DeleteManga
    // fans out N events (one per deleted manga); when an existing exclusion already
    // covers the MangaDexId we early-return without re-inserting.
    public interface IImportListExclusionService
    {
        ImportListExclusion Add(ImportListExclusion importListExclusion);
        List<ImportListExclusion> All();
        PagingSpec<ImportListExclusion> Paged(PagingSpec<ImportListExclusion> pagingSpec);
        void Delete(int id);
        void Delete(List<int> ids);
        ImportListExclusion Get(int id);
        ImportListExclusion FindByMangaDexId(string mangaDexId);
        ImportListExclusion Update(ImportListExclusion importListExclusion);
    }

    public class ImportListExclusionService : IImportListExclusionService, IHandle<MangaDeletedEvent>
    {
        private readonly IImportListExclusionRepository _repo;

        public ImportListExclusionService(IImportListExclusionRepository repo)
        {
            _repo = repo;
        }

        public ImportListExclusion Add(ImportListExclusion importListExclusion)
        {
            return _repo.Insert(importListExclusion);
        }

        public ImportListExclusion Update(ImportListExclusion importListExclusion)
        {
            return _repo.Update(importListExclusion);
        }

        public void Delete(int id)
        {
            _repo.Delete(id);
        }

        public void Delete(List<int> ids)
        {
            _repo.DeleteMany(ids);
        }

        public ImportListExclusion Get(int id)
        {
            return _repo.Get(id);
        }

        public ImportListExclusion FindByMangaDexId(string mangaDexId)
        {
            return _repo.FindByMangaDexId(mangaDexId);
        }

        public List<ImportListExclusion> All()
        {
            return _repo.All().ToList();
        }

        public PagingSpec<ImportListExclusion> Paged(PagingSpec<ImportListExclusion> pagingSpec)
        {
            return _repo.GetPaged(pagingSpec);
        }

        public void Handle(MangaDeletedEvent message)
        {
            // D-12 (Open Q #1): AddImportListExclusion default is true so bulk-delete
            // callers that don't opt out get Sonarr-canonical UX (re-add prevention).
            // Explicit false suppresses the exclusion row (admin tooling, programmatic
            // resync, etc.).
            if (!message.AddImportListExclusion)
            {
                return;
            }

            var manga = message.Manga;
            var mangaDexIdString = manga.MangaDexId?.ToString();

            // Items with NULL MangaDexId AND no MalId/AniListId/MangaBakaId have nothing
            // to exclude by — skip rather than store an all-null row that no finder can
            // reach later. quick-260619-spc: the guard now spans all FOUR identity axes
            // (MangaDexId/MalId/AniListId/MangaBakaId) so a MangaBaka-only manga (the
            // v1.3 default-primary anchor) DOES produce a usable exclusion row.
            if (mangaDexIdString.IsNullOrWhiteSpace() &&
                (manga.MalId ?? 0) == 0 &&
                (manga.AniListId ?? 0) == 0 &&
                (manga.MangaBakaId ?? 0) == 0)
            {
                return;
            }

            // Idempotency guard: if a prior delete already left an exclusion row for
            // this manga, do not insert a duplicate. We must check ALL THREE identity
            // axes (MangaDexId / MalId / AniListId) — CodeRabbit PR #218 finding +
            // outside-diff follow-up — because:
            //   1. MAL-only or AniList-only manga (NULL MangaDexId) bypass any
            //      MangaDexId-only check and accumulate duplicate rows on re-delete.
            //   2. An older/manual exclusion row with only MalId/AniListId can
            //      coexist with a later re-delete that DOES have MangaDexId — the
            //      MangaDexId lookup misses the secondary-only row entirely, so the
            //      checks must run independently (NOT as if/else-if).
            // The UNIQUE-with-NULLs MangaDexId index doesn't help here — only
            // same-MangaDexId duplicates are rejected at the DB level.
            //
            // Phase 27 may want to elevate this to a repository finder
            // (FindByExternalIds(string?, int?, int?)) once provider plugins
            // exist and exclusion volume warrants the optimization; for v1.1
            // substrate-only the in-memory scan over All() is fine.
            if (!mangaDexIdString.IsNullOrWhiteSpace())
            {
                var existing = _repo.FindByMangaDexId(mangaDexIdString);
                if (existing != null)
                {
                    return;
                }
            }

            if ((manga.MalId ?? 0) != 0 || (manga.AniListId ?? 0) != 0 || (manga.MangaBakaId ?? 0) != 0)
            {
                // Secondary-ID path: scan All() for a row matching the same
                // non-zero MalId, AniListId, or MangaBakaId — runs INDEPENDENTLY of
                // the MangaDexId check above so a stale secondary-only exclusion
                // is also caught even when the current delete carries a
                // MangaDexId. quick-260619-spc widened this to MangaBakaId (the
                // v1.3 default-primary anchor) so a MangaBaka-only re-delete does
                // not accumulate duplicate rows. Substrate-only (D-08) means
                // exclusion volume is bounded by user UI activity, not provider
                // sync; the scan is cheap until Phase 27 changes that.
                var existing = _repo.All().FirstOrDefault(x =>
                    ((manga.MalId ?? 0) != 0 && x.MalId == manga.MalId) ||
                    ((manga.AniListId ?? 0) != 0 && x.AniListId == manga.AniListId) ||
                    ((manga.MangaBakaId ?? 0) != 0 && x.MangaBakaId == manga.MangaBakaId));
                if (existing != null)
                {
                    return;
                }
            }

            _repo.Insert(new ImportListExclusion
            {
                MangaDexId = mangaDexIdString,
                MalId = manga.MalId,
                AniListId = manga.AniListId,
                MangaBakaId = manga.MangaBakaId,
                Title = manga.Title
            });
        }
    }
}
