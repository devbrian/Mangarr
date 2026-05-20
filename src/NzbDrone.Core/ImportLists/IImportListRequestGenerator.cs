namespace NzbDrone.Core.ImportLists
{
    // Phase 26 Plan 26-04 — verbatim port from
    // .planning/reference/sonarr-vertical-slices/import-lists/IImportListRequestGenerator.cs.
    public interface IImportListRequestGenerator
    {
        ImportListPageableRequestChain GetListItems();
    }
}
