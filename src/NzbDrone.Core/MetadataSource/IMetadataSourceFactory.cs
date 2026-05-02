using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Factory abstraction for the IMetadataSource ThingiProvider family per D-14.
    /// Adds the IsPrimary invariant operations on top of the standard
    /// <see cref="IProviderFactory{TProvider, TProviderDefinition}"/> surface (D-15).
    /// </summary>
    public interface IMetadataSourceFactory : IProviderFactory<IMetadataSource, MetadataSourceDefinition>
    {
        /// <summary>
        /// Return the single MetadataSourceDefinition with IsPrimary=true. Throws when
        /// none exists (configuration drift) per D-15 + threat T-CONFIG-DRIFT-01.
        /// </summary>
        MetadataSourceDefinition GetPrimary();

        /// <summary>
        /// Promote the metadata source identified by <paramref name="id"/> to primary,
        /// demoting all others atomically per D-15. Throws
        /// <see cref="System.InvalidOperationException"/> when the id is not found.
        /// </summary>
        void SetPrimary(int id);
    }
}
