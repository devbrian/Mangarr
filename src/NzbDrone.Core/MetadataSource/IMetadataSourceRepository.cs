using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Repository abstraction for <see cref="MetadataSourceDefinition"/>. Mirrors
    /// <see cref="Indexers.IIndexerRepository"/> with the addition of
    /// <see cref="GetPrimary"/> for the IsPrimary invariant per D-15.
    /// </summary>
    public interface IMetadataSourceRepository : IProviderRepository<MetadataSourceDefinition>
    {
        MetadataSourceDefinition FindByName(string name);
        MetadataSourceDefinition GetPrimary();
    }
}
