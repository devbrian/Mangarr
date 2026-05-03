using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.IndexerSearch.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-07 — see DIVERGENCE.md.
    // Role-match analog: NzbDrone.Core.Indexers.RssSyncCommand (TV scheduled poll).
    //
    // D-07: scheduled poll for new manga releases. MangaRssSyncService filters
    // _indexerFactory.RssEnabled() to Protocol == DownloadProtocol.Http and
    // honors per-IndexerDefinition.SyncInterval override on top of the global
    // IConfigService.MangaRssSyncInterval Config key (default 15min, mirroring
    // Sonarr's RssSyncInterval default).
    //
    // Registered in TaskManager.defaultTasks at runtime per sonarr-consistency-audit
    // anti-pattern C (NOT seeded via 001 Insert.IntoTable).
    //
    // Phase 8 cleanup: collapse with RssSyncCommand when Tv/ deletes.
    public class MangaRssSyncCommand : Command
    {
        public override bool SendUpdatesToClient => true;
    }
}
