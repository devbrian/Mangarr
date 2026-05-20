using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.ImportLists.Exclusions
{
    // Phase 26 Plan 26-04 (D-13 — repo #3 of 3) — ported from
    // .planning/reference/sonarr-vertical-slices/import-lists/Exclusions/ImportListExclusionRepository.cs
    // with the Sonarr `FindByTvdbId(int)` finder swapped to `FindByMangaDexId(string)`
    // per the Migration 003 manga-ID triplet column shape.
    //
    // SingleOrDefault preserves the upstream contract — multiple NULL MangaDexId rows
    // (AniList-only / MAL-only exclusions) coexist per SQLite UNIQUE-with-NULLs
    // semantics, but the finder is only called for non-null Guid.ToString() lookups so
    // the index hit is unique-by-design.
    public interface IImportListExclusionRepository : IBasicRepository<ImportListExclusion>
    {
        ImportListExclusion FindByMangaDexId(string mangaDexId);
    }

    public class ImportListExclusionRepository : BasicRepository<ImportListExclusion>, IImportListExclusionRepository
    {
        public ImportListExclusionRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public ImportListExclusion FindByMangaDexId(string mangaDexId)
        {
            // CodeRabbit PR #218: a NULL/blank lookup would hit the
            // UNIQUE-with-NULLs index and return multiple rows
            // (AniList-only / MAL-only exclusions all carry NULL MangaDexId).
            // SingleOrDefault would then throw. The contract is "look up by
            // a known MangaDexId" — guard before querying so callers that
            // accidentally pass null/empty get a deterministic null rather
            // than a runtime exception.
            if (string.IsNullOrWhiteSpace(mangaDexId))
            {
                return null;
            }

            return Query(m => m.MangaDexId == mangaDexId).SingleOrDefault();
        }
    }
}
