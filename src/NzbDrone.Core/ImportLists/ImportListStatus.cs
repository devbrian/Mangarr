using System;
using NzbDrone.Core.ThingiProvider.Status;

namespace NzbDrone.Core.ImportLists
{
    // Phase 26 Plan 26-04 — verbatim port from
    // .planning/reference/sonarr-vertical-slices/import-lists/ImportListStatus.cs.
    // Inherits ProviderStatusBase escalation/backoff infrastructure; adds the two
    // ImportList-specific fields (LastInfoSync + HasRemovedItemSinceLastClean) consumed
    // by ImportListStatusService.UpdateListSyncStatus / MarkListsAsCleaned and by
    // FetchAndParseImportListService.Fetch's per-list MinRefreshInterval guard.
    public class ImportListStatus : ProviderStatusBase
    {
        public DateTime? LastInfoSync { get; set; }
        public bool HasRemovedItemSinceLastClean { get; set; }
    }
}
