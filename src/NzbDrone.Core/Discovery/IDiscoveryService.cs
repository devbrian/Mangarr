using System.Collections.Generic;
using NzbDrone.Core.MetadataSource.MangaBaka.Resource;

namespace NzbDrone.Core.Discovery
{
    /// <summary>
    /// The Discovery domain brain (Plan 42-02). Owns the server-side eligibility auto-paging loop
    /// that browses MangaBaka via the provider pass-throughs (42-01), post-filters each page
    /// against the in-library + exclusion sets (and skips MangaBaka <c>state=merged|deleted</c>
    /// rows — D-12), and surfaces at most X eligible cards plus the pool-exhaustion signal (D-06).
    /// Also exposes the cached + slim genre/tag option lists for the filter UI (D-03).
    ///
    /// <para>
    /// The controller (42-04) is a thin wrapper over this service. Discovery ALWAYS browses
    /// MangaBaka — even when a different metadata source is the active primary — because only
    /// MangaBaka's search supports attribute filters; the service resolves the MangaBaka provider
    /// specifically from the registered metadata-source set, never <c>GetPrimary()</c>.
    /// </para>
    /// </summary>
    public interface IDiscoveryService
    {
        DiscoveryResult Search(DiscoveryFilter filter, int x);
        List<MangaBakaGenre> GetGenres();
        List<MangaBakaTag> GetTags();
    }
}
