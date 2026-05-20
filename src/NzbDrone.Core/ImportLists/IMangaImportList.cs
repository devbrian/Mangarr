using System;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.ImportLists
{
    // Phase 26 Plan 26-04 (IL-02) — ThingiProvider plugin contract for manga import
    // lists. Mangarr peer of Sonarr's `IImportList` (preserved verbatim at
    // .planning/reference/sonarr-vertical-slices/import-lists/IImportList.cs); renamed
    // to `IMangaImportList` per Mangarr's naming convention (peers: IMangaService,
    // IMangaParsingService, MangaSearchCriteria, …).
    //
    // Production providers ship in Phase 27 — Phase 26 ships ZERO concrete IMangaImportList
    // implementations in NzbDrone.Core per D-08 / Pitfall 2 (the test-only TestImportList
    // fake lives in NzbDrone.Core.Test).
    public interface IMangaImportList : IProvider
    {
        ImportListType ListType { get; }
        TimeSpan MinRefreshInterval { get; }
        ImportListFetchResult Fetch();
    }
}
