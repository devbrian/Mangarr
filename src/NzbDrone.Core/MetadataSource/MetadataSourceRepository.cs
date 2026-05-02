using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Dapper repository for <see cref="MetadataSourceDefinition"/>. Inherits the
    /// ProviderRepository read path (Settings JSON deserialization + Polly SQLITE_BUSY
    /// retry funnel via CR-02) automatically. Migration 002 supplies the
    /// <c>MetadataSources</c> table schema.
    /// </summary>
    public class MetadataSourceRepository
        : ProviderRepository<MetadataSourceDefinition>, IMetadataSourceRepository
    {
        public MetadataSourceRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public MetadataSourceDefinition FindByName(string name)
        {
            return Query(d => d.Name == name).SingleOrDefault();
        }

        public MetadataSourceDefinition GetPrimary()
        {
            return Query(d => d.IsPrimary).SingleOrDefault();
        }
    }
}
