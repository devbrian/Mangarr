using Mangarr.Api.V5.Provider;
using Mangarr.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MetadataSource;
using NzbDrone.SignalR;

namespace Mangarr.Api.V5.MetadataSource;

// Phase 2 developer-surface CRUD controller per Plan 02-10. Mirrors IndexerController
// shape verbatim (Mangarr.Api.V5/Indexers/IndexerController.cs) — ProviderControllerBase
// auto-provides Get / Post / Put / Delete / Test / Schema / TestAll / Action endpoints
// against the IMetadataSource ThingiProvider family. Adds the bespoke SetPrimary route
// that delegates to MetadataSourceFactory.SetPrimary and enforces the D-15 at-most-one
// invariant atomically.
//
// Phase 31 (v1.2 — INSERTED 2026-05-24) D-02 (IL2-01 reframe): adds a `new`-modifier
// override of the inherited GET /api/v5/metadatasource/schema endpoint that filters
// out deprecated providers (AniList + MAL post-Phase-31). The filter reads the
// IsDeprecated virtual on MetadataSourceBase via reflection so provider classes
// stay registered (cross-source SearchForNewManga resolver in
// ImportListSyncService.cs:240-244 continues to work) but are hidden from the
// user-facing Settings → MetadataSources Add picker.
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

    /// <summary>
    /// Phase 31 D-02 (IL2-01 reframe) — filter AniList + MAL out of the Settings →
    /// MetadataSources Add picker by reading the IsDeprecated virtual on each
    /// provider's MetadataSourceBase. Provider classes stay registered (cross-source
    /// SearchForNewManga resolver continues to work) but are hidden from the
    /// user-facing schema endpoint.
    ///
    /// The <c>new</c> modifier shadows the inherited base
    /// <see cref="ProviderControllerBase{TProviderResource,TBulkProviderResource,TProvider,TProviderDefinition}.GetTemplates"/>;
    /// we re-emit the base result with the deprecation filter applied. The reflection
    /// read on the IsDeprecated property avoids generic-cast gymnastics against the
    /// closed-generic MetadataSourceBase&lt;TSettings&gt; shape (each concrete
    /// provider binds TSettings to a different settings class).
    /// </summary>
    [HttpGet("schema")]
    [Produces("application/json")]
    public new Ok<List<MetadataSourceResource>> GetTemplates()
    {
        var baseResult = base.GetTemplates();

        var deprecatedImpls = _factory.GetAvailableProviders()
            .Where(p => (bool)(p.GetType().GetProperty("IsDeprecated")?.GetValue(p) ?? false))
            .Select(p => p.GetType().Name)
            .ToHashSet();

        var filtered = (baseResult.Value ?? new List<MetadataSourceResource>())
            .Where(r => !deprecatedImpls.Contains(r.Implementation ?? string.Empty))
            .ToList();

        return TypedResults.Ok(filtered);
    }
}
