using Mangarr.Api.V5.Provider;
using Mangarr.Http;
using NzbDrone.Core.ImportLists;
using NzbDrone.SignalR;

namespace Mangarr.Api.V5.ImportLists;

// D-14: authored from src/Mangarr.Api.V5/Indexers/IndexerController.cs:11 17-line
// ProviderControllerBase template; main controller NOT preserved in reference slice
// (.planning/reference/sonarr-vertical-slices/import-lists/v5-controller/ contains
// only the Exclusion family — ExclusionController + ExclusionResource +
// ExclusionBulkResource + ExclusionExistsValidator).
//
// Inherits the full 10-endpoint CRUD surface from ProviderControllerBase (verified
// in 26-RESEARCH.md §Q5):
//   GET    /api/v5/importlist          (list)
//   GET    /api/v5/importlist/{id}     (by-id)
//   POST   /api/v5/importlist          (create)
//   PUT    /api/v5/importlist/{id}     (update)
//   DELETE /api/v5/importlist/{id}     (delete)
//   GET    /api/v5/importlist/schema   (provider templates — returns [] until Phase 27)
//   POST   /api/v5/importlist/test     (test definition)
//   POST   /api/v5/importlist/testall  (test all enabled)
//   PUT    /api/v5/importlist/bulk     (bulk update)
//   DELETE /api/v5/importlist/bulk     (bulk delete)
//
// Phase 26 substrate ships ZERO field-level SharedValidator rules per D-08 — Phase 27
// providers add per-Settings rules in their own POCO classes. The base validator
// rules (Name not empty / unique, Implementation + ConfigContract not empty,
// Fields not null) come from ProviderControllerBase ctor at lines 44-49.
[V5ApiController]
public class ImportListController : ProviderControllerBase<ImportListResource, ImportListBulkResource, IMangaImportList, ImportListDefinition>
{
    public static readonly ImportListResourceMapper ResourceMapper = new();
    public static readonly ImportListBulkResourceMapper BulkResourceMapper = new();

    public ImportListController(IBroadcastSignalRMessage signalRBroadcaster, IImportListFactory importListFactory)
        : base(signalRBroadcaster, importListFactory, "importlist", ResourceMapper, BulkResourceMapper)
    {
        // Phase 26 substrate ships ZERO field-level SharedValidator rules per D-08.
        // Phase 27 providers add per-Settings rules in their own POCO classes.
    }
}
