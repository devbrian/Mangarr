using System;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download.Pending.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 9 D-09-06 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Download/Pending/PendingRelease.cs.
    //
    // Manga port of TV PendingRelease. PendingReleaseAdditionalInfo is OMITTED — TV's
    // SeriesMatchType + ReleaseSourceType have no manga analog per D-09-06 manga-shape.
    // The MangaPendingReleases table created in Migration 001 has no AdditionalInfo column.
    //
    // BL-01 GUARD: a separate Mapper.Entity registration in TableMapping.cs binds this
    // POCO to the MangaPendingReleases table — physically impossible to leak rows from
    // the TV-side PendingReleases table even when MangaId / SeriesId int values collide.
    // Mirrors MangaBlocklist precedent at MangaBlocklistRepository.cs:11-15.
    //
    // Phase 14 cleanup: collapse with PendingRelease when Tv/ deletes.
    public class MangaPendingRelease : ModelBase
    {
        public int MangaId { get; set; }
        public string Title { get; set; }
        public DateTime Added { get; set; }
        public ParsedChapterInfo ParsedChapterInfo { get; set; }    // ← manga substitution (was ParsedEpisodeInfo)
        public ReleaseInfo Release { get; set; }                    // ← REUSE (media-agnostic)
        public PendingReleaseReason Reason { get; set; }            // ← REUSE (enum is media-agnostic)

        // Not persisted — populated by service projection (Plan 09-10).
        public RemoteChapter RemoteChapter { get; set; }
    }
}
