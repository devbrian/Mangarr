using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Metadata
{
    // Phase 30 Plan 30-04 D-02 — Dapper repository for MetadataDefinition rows.
    // Ports src/NzbDrone.Core/ImportLists/ImportListRepository.cs:16-38 verbatim
    // with Metadata type substitution. Inherits CR-02 SQLITE_BUSY retry wrap from
    // ProviderRepository<T> automatically.
    //
    // R-10 RESOLVED: the `Metadata` table is created by 001_mangarr_baseline.cs:129-134
    // (5 columns: Enable, Name, Implementation, Settings, ConfigContract). Migration
    // 004 (Plan 30-05) added the conditional ComicInfoMetadata seed row when
    // Config.MetadataFormats contained "comicinfo" (D-03 preserves user state).
    public interface IMetadataRepository : IProviderRepository<MetadataDefinition>
    {
    }

    public class MetadataRepository : ProviderRepository<MetadataDefinition>, IMetadataRepository
    {
        public MetadataRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }
}
