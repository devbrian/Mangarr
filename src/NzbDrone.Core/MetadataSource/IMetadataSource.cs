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
    }
}
