using System.Collections.Generic;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Blocklisting.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-11 + D-19 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Blocklisting/BlocklistService.cs (IBlocklistService).
    //
    // Pitfall 5 GUARD on Blocklisted(int, ReleaseInfo): implementations MUST use
    //   - trim + lowercase Title both sides
    //   - OrdinalIgnoreCase string comparisons
    //   - null-tolerant SourceKey fallback (treat empty/null as match-on-other-fields-only)
    // so the auto-retry loop (Plan 06-08) cannot pick up a release just blocklisted because of
    // case/whitespace/null variation in the (SourceKey, ReleaseGuid, Title) triple.
    //
    // Phase 8 cleanup: collapse with IBlocklistService when Tv/ deletes.
    public interface IMangaBlocklistService
    {
        bool Blocklisted(int mangaId, ReleaseInfo release);
        PagingSpec<MangaBlocklist> Paged(PagingSpec<MangaBlocklist> pagingSpec);
        void Block(MangaBlocklist blocklist);
        void Delete(int id);
        void Delete(List<int> ids);
    }
}
