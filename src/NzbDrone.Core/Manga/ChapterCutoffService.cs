using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Profiles.CustomFormats;
using NzbDrone.Core.Profiles.Translations;

namespace NzbDrone.Core.Manga
{
    public interface IChapterCutoffService
    {
        PagingSpec<Chapter> ChaptersWhereCutoffUnmet(PagingSpec<Chapter> pagingSpec);
    }

    // Phase 8 audit (no-sibling/EpisodeCutoffService.md). Sibling of
    // src/NzbDrone.Core/Tv/EpisodeCutoffService.cs — drives the V5 Wanted/Cutoff
    // feed that surfaces chapters whose imported file is below the user's preferred
    // language tier or CF score gate.
    //
    // Manga-axis divergence from TV: TV's single QualityProfile carries cutoff +
    // upgrade gating in one entity. Phase 5 D-01 + D-07 split that across two
    // sibling profile entities — TranslationProfile (language rank, outer gate)
    // and CustomFormatProfile (CF score, inner gate). This service computes the
    // "below cutoff" set per profile and delegates the paged query to the
    // ChapterRepository, mirroring TV's EpisodeCutoffService.EpisodesWhereCutoffUnmet
    // -> EpisodeRepository.EpisodesWhereCutoffUnmet two-piece architecture verbatim.
    //
    // Cutoff semantics:
    //   * TranslationProfile is "below cutoff" when its Languages list has more than
    //     one entry — i.e. there is at least one preference rank below the top tier
    //     where a chapter file could land short of the user's preferred language.
    //     A single-language profile (e.g. the seeded "English Only" default) has
    //     no language to upgrade to, so its profile id is NOT in the below-cutoff set.
    //   * CustomFormatProfile is "below cutoff" when its MinFormatScore > 0 OR when
    //     MaxFormatScore is a positive cap — i.e. there is a score window the user
    //     wants enforced. A profile with MinFormatScore=0 + MaxFormatScore=null/0
    //     (the seeded default, or a blank max field) accepts every score and is NOT
    //     in the below-cutoff set. (0 = no cap, consistent with the search-time spec.)
    //
    // The repository then narrows the paged Chapter query to rows whose Manga is
    // assigned to ANY profile id in the below-cutoff sets. The Wanted/Cutoff feed
    // consumer (Phase 6 WANTED-02 V5 controller — out of scope here) re-evaluates
    // each candidate via the manga DecisionEngine when the user acts.
    //
    // Phase 8 cleanup: collapse with EpisodeCutoffService when Tv/ deletes.
    public class ChapterCutoffService : IChapterCutoffService
    {
        private readonly IChapterRepository _chapterRepository;
        private readonly ITranslationProfileService _translationProfileService;
        private readonly ICustomFormatProfileService _customFormatProfileService;
        private readonly Logger _logger;

        public ChapterCutoffService(IChapterRepository chapterRepository,
                                    ITranslationProfileService translationProfileService,
                                    ICustomFormatProfileService customFormatProfileService,
                                    Logger logger)
        {
            _chapterRepository = chapterRepository;
            _translationProfileService = translationProfileService;
            _customFormatProfileService = customFormatProfileService;
            _logger = logger;
        }

        public PagingSpec<Chapter> ChaptersWhereCutoffUnmet(PagingSpec<Chapter> pagingSpec)
        {
            var belowCutoffTranslationProfileIds = new List<int>();
            var translationProfiles = _translationProfileService.All();

            foreach (var profile in translationProfiles)
            {
                // Multi-language profile = at least one rank below the top tier exists.
                // Single-language profile has no upgrade target — not below-cutoff.
                if (profile.Languages != null && profile.Languages.Count > 1)
                {
                    belowCutoffTranslationProfileIds.Add(profile.Id);
                }
            }

            var belowCutoffCustomFormatProfileIds = new List<int>();
            var customFormatProfiles = _customFormatProfileService.All();

            foreach (var profile in customFormatProfiles)
            {
                // Score window enforced (min > 0 OR max capped) means a file could land
                // outside the window and need upgrading. The seeded default
                // (MinFormatScore=0 + MaxFormatScore=null) accepts every score and is excluded.
                // A MaxFormatScore of 0 means "no cap" (same as null) — a profile saved with the
                // max field blank persists 0, so 0 must NOT count as an enforced upper bound here
                // (consistent with CustomFormatMinimumScoreSpecification).
                if (profile.MinFormatScore > 0 || profile.MaxFormatScore is > 0)
                {
                    belowCutoffCustomFormatProfileIds.Add(profile.Id);
                }
            }

            if (!belowCutoffTranslationProfileIds.Any() && !belowCutoffCustomFormatProfileIds.Any())
            {
                _logger.Trace("No translation or custom-format profiles are below cutoff; returning empty cutoff-unmet feed");
                pagingSpec.Records = new List<Chapter>();
                pagingSpec.TotalRecords = 0;
                return pagingSpec;
            }

            return _chapterRepository.ChaptersWhereCutoffUnmet(pagingSpec,
                                                               belowCutoffTranslationProfileIds,
                                                               belowCutoffCustomFormatProfileIds);
        }
    }
}
