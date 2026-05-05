using NLog;
using NzbDrone.Core.Parser.Manga.Model;

namespace NzbDrone.Core.DecisionEngine.Manga.Specifications
{
    // Sonarr divergence: NEW manga sibling per Phase 8 backfill (audit gap
    // no-sibling/SeriesSpecification). Mirrors TV
    // DecisionEngine/Specifications/Search/SeriesSpecification.cs verbatim shape
    // with RemoteEpisode -> RemoteChapter and SearchCriteria -> MangaSearchCriteria.
    //
    // Role: during a user-initiated search the parsed RemoteChapter.Manga.Id MUST
    // match the search target's MangaSearchCriteria.Manga.Id. Guards the real bug
    // surface where MangaParsingService.GetManga can resolve the wrong Manga from
    // a release title (title collision between two manga) and let the wrong-manga
    // release leak through the search-decision pipeline. The MonitoredMangaSpec
    // short-circuits the monitored check during search so without THIS spec there
    // is NO id-cross-check on the manga search path.
    //
    // Scope decisions (vs audit's broader outline):
    //   * Lives flat under DecisionEngine/Manga/Specifications/ — no Search/ subdir
    //     on the manga side (sibling specs like ChapterRequestedSpecification are
    //     top level; cluster-02 audit reaffirmed flat layout).
    //   * Reuses DownloadRejectionReason.MatchesAnotherSeries — closest existing
    //     semantic analog ("release matches a different entity than search target").
    //     NO new reject enum value (Phase 8 charter: no new infrastructure types).
    //
    // Type=Permanent mirrors the TV original (line 16): a wrong-manga release will
    // never become a right-manga release — there is no point retrying.
    //
    // Phase 8 cleanup: collapse with TV analog when Tv/ deletes.
    public class MangaSpecification : IMangaDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public MangaSpecification(Logger logger)
        {
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public DownloadSpecDecision IsSatisfiedBy(RemoteChapter subject, ReleaseDecisionInformation information)
        {
            var searchCriteria = information?.MangaSearchCriteria;

            if (searchCriteria == null)
            {
                // Not a search — RSS path; not our concern.
                return DownloadSpecDecision.Accept();
            }

            _logger.Debug("Checking if manga matches searched manga");

            if (subject.Manga.Id != searchCriteria.Manga.Id)
            {
                _logger.Debug("Manga {0} does not match {1}", subject.Manga, searchCriteria.Manga);
                return DownloadSpecDecision.Reject(DownloadRejectionReason.MatchesAnotherSeries, "Wrong manga");
            }

            return DownloadSpecDecision.Accept();
        }
    }
}
