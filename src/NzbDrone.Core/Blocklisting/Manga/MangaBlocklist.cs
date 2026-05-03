using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Blocklisting.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-11 + D-19 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Blocklisting/Blocklist.cs.
    //
    // D-11 release-identity triple: (SourceKey, ReleaseGuid, SourceTitle) — distinct from TV's
    // (Title, Indexer, Protocol, TorrentInfoHash) quintuple. Manga has no Quality, no Protocol
    // (in v1 only DownloadProtocol.Http is in play), and no TorrentInfoHash. The triple plus
    // MangaId + ChapterIds is enough to dedup blocklist rows uniquely.
    //
    // SourceKey is the per-Phase-3 indexer canonical key (e.g. "MangaDex", "comix.to" — NOT the
    // user-facing display name). ReleaseInfo.Indexer carries this value at the wire layer per
    // existing ChapterHistoryService.Handle precedent (Plan 06-03 line 141).
    //
    // Phase 8 cleanup: collapse with src/NzbDrone.Core/Blocklisting/Blocklist.cs when Tv/ deletes.
    public class MangaBlocklist : ModelBase
    {
        public int MangaId { get; set; }
        public List<int> ChapterIds { get; set; } = new List<int>();
        public string SourceTitle { get; set; }
        public string SourceKey { get; set; }
        public string ReleaseGuid { get; set; }
        public string ReleaseInfoJson { get; set; }
        public DateTime Date { get; set; }
        public string Reason { get; set; }
        public string Source { get; set; }
    }
}
