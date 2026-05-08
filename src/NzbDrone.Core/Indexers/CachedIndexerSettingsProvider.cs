using System;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.Indexers;

public interface ICachedIndexerSettingsProvider
{
    CachedIndexerSettings GetSettings(int indexerId);
}

public class CachedIndexerSettingsProvider : ICachedIndexerSettingsProvider, IHandle<ProviderUpdatedEvent<IIndexer>>, IHandle<ProviderDeletedEvent<IIndexer>>
{
    private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(CachedIndexerSettingsProvider));

    private readonly IIndexerFactory _indexerFactory;
    private readonly ICached<CachedIndexerSettings> _cache;

    public CachedIndexerSettingsProvider(IIndexerFactory indexerFactory, ICacheManager cacheManager)
    {
        _indexerFactory = indexerFactory;
        _cache = cacheManager.GetRollingCache<CachedIndexerSettings>(GetType(), "settingsByIndexer", TimeSpan.FromHours(1));
    }

    public CachedIndexerSettings GetSettings(int indexerId)
    {
        if (indexerId == 0)
        {
            return null;
        }

        return _cache.Get(indexerId.ToString(), () => FetchIndexerSettings(indexerId));
    }

    private CachedIndexerSettings FetchIndexerSettings(int indexerId)
    {
        var indexer = _indexerFactory.Find(indexerId);

        if (indexer?.Settings is not IIndexerSettings indexerSettings)
        {
            Logger.Trace("Could not load settings for indexer ID: {0}", indexerId);

            return null;
        }

        // Sonarr divergence: Phase 15 Plan 15-04 cascade absorption — FailDownloads enum +
        // ITorrentIndexerSettings.SeedCriteria stripped per Plan 15-04 TV indexers DELETE.
        return new CachedIndexerSettings();
    }

    public void Handle(ProviderUpdatedEvent<IIndexer> message)
    {
        _cache.Clear();
    }

    public void Handle(ProviderDeletedEvent<IIndexer> message)
    {
        _cache.Clear();
    }
}

// Sonarr divergence: Phase 15 Plan 15-04 cascade absorption — FailDownloads enum +
// SeedCriteriaSettings stripped per Plan 15-04 TV indexers DELETE (Newznab/Nyaa/etc.)
// + TorrentSeedConfiguration DELETE. Manga uses HTTP-only protocol (no torrent seeding;
// no Usenet retry shape).
public class CachedIndexerSettings
{
}
