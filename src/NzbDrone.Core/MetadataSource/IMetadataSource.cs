using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Phase 2 ThingiProvider-discoverable composite interface per D-14 + Pitfall 4.
    /// Concrete providers MUST implement <see cref="IMetadataSource"/> (not just the two
    /// split contracts) so DryIoc registers them as a single family. Signature mismatch =
    /// silent disappearance from <c>MetadataSourceFactory</c> listings.
    /// </summary>
    public interface IMetadataSource : IProvider, IProvideMangaInfo, ISearchForNewManga
    {
        /// <summary>
        /// Phase 31 D-02 (IL2-01 reframe) — providers returning true are excluded from
        /// the GET /api/v5/metadatasource/schema response (hidden from the Settings →
        /// MetadataSources Add picker). Provider class stays registered for cross-source
        /// SearchForNewManga lookups via the resolver in
        /// ImportListSyncService.cs:240-244 (D-05). Default false on MetadataSourceBase;
        /// AniList + MAL override to true.
        ///
        /// Phase 31 fix-forward (REVIEW.md §CR-01 remediation): hoisted from the abstract
        /// base onto the interface so the V5 schema-emit filter
        /// (MetadataSourceController.GetTemplates) can iterate REGISTERED TYPES rather
        /// than persisted active rows. The pre-fix controller queried
        /// _factory.GetAvailableProviders() which returns Active().Select(GetInstance) —
        /// post-Migration-005 (AniList + MAL rows deleted) that returns only MangaDex →
        /// deprecatedImpls is an empty HashSet → filter becomes a no-op. Iterating
        /// IEnumerable&lt;IMetadataSource&gt; (DryIoc-resolved registered types) is the
        /// correct source-set for the Add-picker filter.
        /// </summary>
        bool IsDeprecated { get; }
    }
}
