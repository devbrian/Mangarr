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

            // Items with NULL MangaDexId AND no MalId/AniListId have nothing to exclude
            // by — skip rather than store an all-null row that the FindByMangaDexId
            // finder cannot reach later.
            if (mangaDexIdString.IsNullOrWhiteSpace() &&
                (manga.MalId ?? 0) == 0 &&
                (manga.AniListId ?? 0) == 0)
            {
                return;
            }

            // Idempotency guard: if a prior delete already left an exclusion row for
            // this MangaDexId, do not insert a duplicate (the UNIQUE index on
            // MangaDexId would reject it; the early-return keeps the log clean).
            if (mangaDexIdString != null)
            {
                var existing = _repo.FindByMangaDexId(mangaDexIdString);
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
                Title = manga.Title
            });
        }
    }
}
