using NzbDrone.Core.Parser.Manga.Model;

namespace NzbDrone.Core.DecisionEngine.Manga.Aggregators
{
    // Manga sibling of TV's IAggregateRemoteEpisode
    // (src/NzbDrone.Core/Download/Aggregation/Aggregators/IAggregateRemoteEpisode.cs).
    // Pluggable enricher seam — concrete implementations augment a RemoteChapter
    // (typically Languages/TranslatedLanguage fallback) BEFORE decision-engine
    // specs evaluate the release. DryIoc auto-discovery enumerates registered
    // implementations; the aggregation service iterates and applies each in turn
    // with TV's catch-and-warn loop semantics.
    //
    // v1 has no concrete implementation wired yet — this iteration adds the
    // interface only (Phase 8 backfill 14-01, audit gap
    // .planning/phases/08-tv-manga-parity-audit/audit/no-sibling/IAggregateRemoteEpisode.md).
    // Subsequent iterations add AggregateLanguagesForChapter,
    // RemoteChapterAggregationService, and MangaDownloadDecisionMaker wiring.
    public interface IAggregateRemoteChapter
    {
        RemoteChapter Aggregate(RemoteChapter remoteChapter);
    }
}
