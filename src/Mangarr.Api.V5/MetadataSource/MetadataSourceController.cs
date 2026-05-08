using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MetadataSource;
using NzbDrone.SignalR;
using Mangarr.Api.V5.Provider;
using Sonarr.Http;

namespace Mangarr.Api.V5.MetadataSource;

// Phase 2 developer-surface CRUD controller per Plan 02-10. Mirrors IndexerController
// shape verbatim (Mangarr.Api.V5/Indexers/IndexerController.cs) — ProviderControllerBase
// auto-provides Get / Post / Put / Delete / Test / Schema / TestAll / Action endpoints
// against the IMetadataSource ThingiProvider family. Adds the bespoke SetPrimary route
// that delegates to MetadataSourceFactory.SetPrimary and enforces the D-15 at-most-one
// invariant atomically.
[V5ApiController]
public class MetadataSourceController
    : ProviderControllerBase<MetadataSourceResource, MetadataSourceBulkResource, IMetadataSource, MetadataSourceDefinition>
{
    public static readonly MetadataSourceResourceMapper ResourceMapper = new();
    public static readonly MetadataSourceBulkResourceMapper BulkResourceMapper = new();

    private readonly IMetadataSourceFactory _factory;

    public MetadataSourceController(IBroadcastSignalRMessage signalRBroadcaster,
                                    IMetadataSourceFactory factory)
        : base(signalRBroadcaster, factory, "metadatasource", ResourceMapper, BulkResourceMapper)
    {
        _factory = factory;

        // No extra validators — D-15 IsPrimary at-most-one invariant is enforced inside
        // MetadataSourceFactory.SetPrimary (atomic demote-all-then-promote-one).
    }

    /// <summary>
    /// Promote a single metadata source to primary per D-15. Demotes all others atomically.
    /// Routes through <see cref="IMetadataSourceFactory.SetPrimary(int)"/> to ensure the
    /// at-most-one invariant is preserved — the route handler does NOT mutate IsPrimary
    /// fields directly (threat T-CONFIG-DRIFT-01 mitigation).
    /// </summary>
    [HttpPost("{id}/setprimary")]
    public Results<NoContent, NotFound> SetPrimary(int id)
    {
        try
        {
            _factory.SetPrimary(id);
            return TypedResults.NoContent();
        }
        catch (InvalidOperationException)
        {
            return TypedResults.NotFound();
        }
    }
}
