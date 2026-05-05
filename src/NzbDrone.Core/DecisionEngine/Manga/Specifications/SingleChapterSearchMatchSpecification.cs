using System.Linq;
using NLog;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Manga.Model;

namespace NzbDrone.Core.DecisionEngine.Manga.Specifications
{
    // Sonarr divergence: NEW manga sibling per Phase 8 backfill (audit gap
    // no-sibling/SingleEpisodeSearchMatchSpecification). Mirrors TV
    // DecisionEngine/Specifications/Search/SingleEpisodeSearchMatchSpecification.cs
    // with RemoteEpisode -> RemoteChapter and SearchCriteria -> MangaSearchCriteria.
    //
    // Role: during a single-chapter search, reject releases whose
    // ParsedChapterInfo.ChapterNumbers set does NOT contain the requested chapter
    // number. Avoids grabbing a multi-chapter pack when the user asked for one
    // specific chapter (the inverse of ChapterRequestedSpecification, which only
    // rejects releases containing NONE of the requested chapter ids — coarser).
    //
    // Scope decisions (vs audit's broader outline):
    //   * Lives flat under DecisionEngine/Manga/Specifications/ — no Search/ subdir
    //     on the manga side (sibling specs like ChapterRequestedSpecification +
    //     MangaSpecification are top level; cluster-02 audit reaffirmed flat layout).
    //   * Reuses DownloadRejectionReason.ChapterMissingFromRelease — semantically
    //     exact match for "requested chapter is not in the release's parsed chapter
    //     set." NO new reject enum value (Phase 8 charter: no new infrastructure
    //     types). The TV analog uses WrongEpisode for the same axis; manga gets the
    //     manga-shaped equivalent.
    //   * The Season-axis branches and the FullSeason-during-single-search branch
    //     of the TV original are tv_only per D-13 (manga has no Volume / Season).
    //     Only the chapter-axis (WrongEpisode) branch is in-scope for manga.
    //   * Whole-manga searches (MangaSearchCriteria base shape, NOT
    //     ChapterSearchCriteria) skip this spec — caller has already narrowed to
    //     "all monitored chapters" and per-chapter filtering is the caller's job.
    //
    // Type=Permanent mirrors the TV original: a multi-chapter pack will never
    // become a single-chapter release — there is no point retrying.
    //
    // Phase 8 cleanup: collapse with TV analog when Tv/ deletes.
    public class SingleChapterSearchMatchSpecification : IMangaDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public SingleChapterSearchMatchSpecification(Logger logger)
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

            if (searchCriteria is not ChapterSearchCriteria singleChapterSpec)
            {
                // Whole-manga search (MangaSearchCriteria base shape) — caller already
                // narrowed to monitored chapters; per-chapter axis-match doesn't apply.
                return DownloadSpecDecision.Accept();
            }

            var requestedChapter = singleChapterSpec.ChapterNumber;
            var parsedNumbers = subject.ParsedChapterInfo?.ChapterNumbers;

            if (parsedNumbers == null || parsedNumbers.Length == 0)
            {
                _logger.Debug("Release has no parsed chapter numbers during single chapter search, skipping.");
                return DownloadSpecDecision.Reject(DownloadRejectionReason.ChapterMissingFromRelease, "No chapter number in release");
            }

            if (!parsedNumbers.Contains(requestedChapter))
            {
                _logger.Debug("Chapter number {0} does not match searched chapter number {1}, skipping.", string.Join(",", parsedNumbers), requestedChapter);
                return DownloadSpecDecision.Reject(DownloadRejectionReason.ChapterMissingFromRelease, "Wrong chapter");
            }

            return DownloadSpecDecision.Accept();
        }
    }
}
