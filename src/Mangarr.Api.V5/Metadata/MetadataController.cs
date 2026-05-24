using Mangarr.Api.V5.Provider;
using Mangarr.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Metadata;
using NzbDrone.SignalR;

namespace Mangarr.Api.V5.Metadata;

// Phase 30 Plan 30-04 D-02 — V5 ProviderControllerBase subclass for the
// IMetadata ThingiProvider family (closes the long-standing Settings/Metadata
// 404 since Phase 15 deleted V3). Ported line-by-line from
// src/Mangarr.Api.V5/Connections/ConnectionController.cs (closer template than
// ImportListController because Metadata has no FK validators per PATTERNS.md
// §Plan 30-04).
//
// Inherits the 8-active-endpoint CRUD + schema + test + testall surface from
// ProviderControllerBase (Phase 26 D-14 list):
//   GET    /api/v5/metadata           (list)
//   GET    /api/v5/metadata/{id}      (by-id)
//   POST   /api/v5/metadata           (create)
//   PUT    /api/v5/metadata/{id}      (update)
//   DELETE /api/v5/metadata/{id}      (delete)
//   GET    /api/v5/metadata/schema    (provider templates)
//   POST   /api/v5/metadata/test      (test definition)
//   POST   /api/v5/metadata/testall   (test all enabled)
//
// Bulk endpoints disabled via [NonAction] per ConnectionController precedent:
//   PUT    /api/v5/metadata/bulk      → throws NotImplementedException
//   DELETE /api/v5/metadata/bulk      → throws NotImplementedException
//
// Settings/Metadata is single-instance UX (D-04) so bulk operations have no
// real use case; matches the Connections (notifications) precedent.
[V5ApiController]
public class MetadataController : ProviderControllerBase<MetadataResource, MetadataBulkResource, IMetadata, MetadataDefinition>
{
    public static readonly MetadataResourceMapper ResourceMapper = new();
    public static readonly MetadataBulkResourceMapper BulkResourceMapper = new();

    public MetadataController(IBroadcastSignalRMessage signalRBroadcaster, IMetadataFactory metadataFactory)
        : base(signalRBroadcaster, metadataFactory, "metadata", ResourceMapper, BulkResourceMapper)
    {
    }

    [NonAction]
    public override Results<Ok<IEnumerable<MetadataResource>>, BadRequest> UpdateProvider([FromBody] MetadataBulkResource providerResource)
    {
        throw new NotImplementedException();
    }

    [NonAction]
    public override NoContent DeleteProviders([FromBody] MetadataBulkResource resource)
    {
        throw new NotImplementedException();
    }
}
