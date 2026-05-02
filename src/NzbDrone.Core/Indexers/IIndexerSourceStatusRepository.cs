using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Indexers
{
    /// <summary>
    /// Per-SourceKey indexer status repository (Phase 3 D-17). Sibling to
    /// <see cref="IIndexerStatusRepository"/> (per-ProviderId TV path), keyed on the string
    /// <see cref="IndexerSourceStatus.SourceKey"/> instead of the int ProviderId.
    ///
    /// Analog: <see cref="IndexerStatusRepository"/>.
    /// </summary>
    public interface IIndexerSourceStatusRepository : IBasicRepository<IndexerSourceStatus>
    {
        IndexerSourceStatus FindBySourceKey(string sourceKey);
        void DeleteBySourceKey(string sourceKey);
    }

    public class IndexerSourceStatusRepository : BasicRepository<IndexerSourceStatus>, IIndexerSourceStatusRepository
    {
        public IndexerSourceStatusRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public IndexerSourceStatus FindBySourceKey(string sourceKey)
        {
            return Query(c => c.SourceKey == sourceKey).SingleOrDefault();
        }

        public void DeleteBySourceKey(string sourceKey)
        {
            Delete(c => c.SourceKey == sourceKey);
        }
    }
}
