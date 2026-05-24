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
// Phase 31 (v1.2 — INSERTED 2026-05-24) D-02 (IL2-01 reframe): adds an override of the
// inherited GET /api/v5/metadatasource/schema endpoint that filters out deprecated
// providers (AniList + MAL post-Phase-31). The filter reads the IsDeprecated property
// on each registered IMetadataSource so provider classes stay registered (cross-source
// SearchForNewManga resolver in ImportListSyncService.cs:240-244 continues to work)
// but are hidden from the user-facing Settings → MetadataSources Add picker.
//
// Phase 31 fix-forward (REVIEW.md §CR-01 remediation, 2026-05-24): the filter iterates
// the DI-injected IEnumerable<IMetadataSource> (registered TYPES) rather than the
// factory's GetAvailableProviders() (persisted active definitions). The pre-fix shape
// was a no-op after Migration 005 deletes AniList + MAL rows: Active() returned only
// MangaDex → deprecatedImpls was an empty HashSet → filter passed every base entry
// through unchanged → AniList + MAL still appeared in the Add picker. Iterating the
// registered-type list (DryIoc resolves all IMetadataSource impls regardless of DB
// state) makes the filter work on fresh DBs and post-Migration-005 DBs equally.
[V5ApiController]
public class MetadataSourceController
    : ProviderControllerBase<MetadataSourceResource, MetadataSourceBulkResource, IMetadataSource, MetadataSourceDefinition>
{
    public static readonly MetadataSourceResourceMapper ResourceMapper = new();
    public static readonly MetadataSourceBulkResourceMapper BulkResourceMapper = new();

    private readonly IMetadataSourceFactory _factory;
    private readonly IEnumerable<IMetadataSource> _providers;

    public MetadataSourceController(IBroadcastSignalRMessage signalRBroadcaster,
                                    IMetadataSourceFactory factory,
                                    IEnumerable<IMetadataSource> providers)
        : base(signalRBroadcaster, factory, "metadatasource", ResourceMapper, BulkResourceMapper)
    {
        _factory = factory;
        _providers = providers;

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
    /// MetadataSources Add picker by reading the IsDeprecated property on each
    /// registered IMetadataSource. Provider classes stay registered (cross-source
    /// SearchForNewManga resolver continues to work) but are hidden from the
    /// user-facing schema endpoint.
    ///
    /// The <c>override</c> chain (paired with <c>virtual</c> on
    /// <see cref="ProviderControllerBase{TProviderResource,TBulkProviderResource,TProvider,TProviderDefinition}.GetTemplates"/>)
    /// eliminates the route-shadow ambiguity that the prior <c>new</c>-modifier shape
    /// risked at ASP.NET MVC action discovery (REVIEW.md §WR-01 remediation, 2026-05-24).
    ///
    /// Phase 31 fix-forward (REVIEW.md §CR-01 remediation, 2026-05-24): iterates the
    /// DI-injected <see cref="IEnumerable{IMetadataSource}"/> (registered TYPES from
    /// DryIoc) rather than the factory's GetAvailableProviders() (persisted active
    /// definitions). Iterating registered types makes the filter work on fresh DBs and
    /// post-Migration-005 DBs equally — the prior shape was a no-op when no AniList /
    /// MAL row was persisted (because Active() returned only MangaDex →
    /// deprecatedImpls was an empty HashSet).
    /// </summary>
    [HttpGet("schema")]
    [Produces("application/json")]
    public override Ok<List<MetadataSourceResource>> GetTemplates()
    {
        var baseResult = base.GetTemplates();

        var deprecatedImpls = _providers
            .Where(p => p.IsDeprecated)
            .Select(p => p.GetType().Name)
            .ToHashSet();

        var filtered = (baseResult.Value ?? new List<MetadataSourceResource>())
            .Where(r => !deprecatedImpls.Contains(r.Implementation ?? string.Empty))
            .ToList();

        return TypedResults.Ok(filtered);
    }
}
