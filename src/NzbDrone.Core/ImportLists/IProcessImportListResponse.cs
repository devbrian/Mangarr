using System.Collections.Generic;

namespace NzbDrone.Core.ImportLists
{
    // Phase 26 Plan 26-04 — verbatim port from
    // .planning/reference/sonarr-vertical-slices/import-lists/IProcessImportListResponse.cs.
    // Reference uses `IParseImportListResponse`; preserved verbatim so providers ported
    // from upstream Sonarr derivatives can reuse parser implementations 1:1.
    public interface IParseImportListResponse
    {
        IList<ImportListItemInfo> ParseResponse(ImportListResponse importListResponse);
    }
}
