using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Profiles.Translations;

namespace NzbDrone.Core.DecisionEngine.Manga
{
    // Sonarr divergence: NEW manga comparer per Phase 5 D-08 — see DIVERGENCE.md.
    // Ordering (best -> worst): Language rank -> CF score -> Indexer priority -> Votes -> Age -> Size.
    // Indexer priority promoted above age/size per user direction (source-stability matters
    // more than freshness at human-library scale). Votes (gateway per-release vote count, plumbed
    // in quick-260607-bnf) is a tiebreaker AFTER indexer priority and BEFORE age — higher votes
    // wins among otherwise-equal candidates (quick-260607-cto, user direction). NOTE: TV ordering at DownloadDecisionComparer.cs:30-41
    // is Quality -> CF -> Protocol -> EpisodeCount -> EpisodeNumber -> IndexerPriority -> Peers -> Age -> Size —
    // manga has NO Quality / EpisodeCount / EpisodeNumber / Peers / Protocol concepts.
    //
    // Helper-method shape (CompareBy / CompareByReverse) mirrors DownloadDecisionComparer.cs:46-59
    // verbatim. CompareIndexerPriority direction mirrors DownloadDecisionComparer.cs:66-69 — lower
    // IndexerPriority value = better (CompareByReverse so that lower-IndexerPriority sorts first).
    public class MangaDownloadDecisionComparer : IComparer<MangaDownloadDecision>
    {
        private readonly ITranslationProfileService _translationProfileService;
        private readonly IConfigService _configService;

        public delegate int CompareDelegate(MangaDownloadDecision x, MangaDownloadDecision y);

        public MangaDownloadDecisionComparer(ITranslationProfileService translationProfileService, IConfigService configService)
        {
            _translationProfileService = translationProfileService;
            _configService = configService;
        }

        public int Compare(MangaDownloadDecision x, MangaDownloadDecision y)
        {
            var comparers = new List<CompareDelegate>
            {
                CompareLanguageRank,        // outer gate per D-08; cf-only-walkthrough.md verdict
                CompareCustomFormatScore,   // inner per D-08
                CompareIndexerPriority,     // promoted above age/size per user direction
                CompareVotes,               // higher gateway votes wins (quick-260607-cto)
                CompareAge,                 // newer wins
                CompareSize                 // larger wins (sane manga fallback)
            };

            return comparers.Select(c => c(x, y)).FirstOrDefault(r => r != 0);
        }

        // Mirrors DownloadDecisionComparer.CompareBy<TS,TV> (lines 46-52) verbatim.
        private int CompareBy<TSubject, TValue>(TSubject left, TSubject right, Func<TSubject, TValue> funcValue)
            where TValue : IComparable<TValue>
        {
            var leftValue = funcValue(left);
            var rightValue = funcValue(right);
            return leftValue.CompareTo(rightValue);
        }

        private int CompareByReverse<TSubject, TValue>(TSubject left, TSubject right, Func<TSubject, TValue> funcValue)
            where TValue : IComparable<TValue>
        {
            return CompareBy(left, right, funcValue) * -1;
        }

        // NEW — no TV analog. Lower rank value (closer to start of profile.Languages list) = better.
        // Not in profile (or no profile assigned) sorts LAST (int.MaxValue). CompareByReverse so the
        // lower-rank wins in OrderByDescending — DownloadDecisionPriorizationService.cs:29 uses
        // OrderByDescending which puts items with HIGHER compare value first; reversing the natural
        // CompareTo flips lower-rank to higher-compare-value.
        private int CompareLanguageRank(MangaDownloadDecision x, MangaDownloadDecision y)
        {
            return CompareByReverse(x.RemoteChapter, y.RemoteChapter, rc =>
            {
                var profileId = rc?.Manga?.TranslationProfileId ?? _configService.DefaultTranslationProfileId;
                if (profileId == null)
                {
                    return int.MaxValue;
                }

                var profile = _translationProfileService.Get(profileId.Value);
                if (profile?.Languages == null)
                {
                    return int.MaxValue;
                }

                var releaseLang = rc?.Release?.TranslatedLanguage;
                if (string.IsNullOrEmpty(releaseLang))
                {
                    return int.MaxValue;
                }

                var rank = profile.Languages.FindIndex(l =>
                    string.Equals(l, releaseLang, StringComparison.OrdinalIgnoreCase));
                return rank < 0 ? int.MaxValue : rank;
            });
        }

        // Higher score wins. Mirrors DownloadDecisionComparer.CompareCustomFormatScore (lines 83-86)
        // — CompareBy + OrderByDescending = higher score sorts first.
        // WR-01: defensive null-safety mirroring CompareLanguageRank pattern — comparer is a
        // public IComparer<T> and may be invoked with RemoteChapter instances whose Release is
        // null (legal per the MangaDownloadDecision constructor surface).
        private int CompareCustomFormatScore(MangaDownloadDecision x, MangaDownloadDecision y)
            => CompareBy(x.RemoteChapter, y.RemoteChapter, rc => rc?.CustomFormatScore ?? 0);

        // Mirror DownloadDecisionComparer.cs:66-69 — lower IndexerPriority value = better.
        // CompareByReverse + OrderByDescending = lower IndexerPriority sorts first.
        // WR-01: null-safe — null Release sorts LAST (int.MaxValue worst-priority sentinel).
        private int CompareIndexerPriority(MangaDownloadDecision x, MangaDownloadDecision y)
            => CompareByReverse(x.RemoteChapter?.Release, y.RemoteChapter?.Release, r => r?.IndexerPriority ?? int.MaxValue);

        // Higher votes wins. Gateway per-release vote count (ReleaseInfo.Votes, plumbed in
        // quick-260607-bnf; defaults to 0 when an older gateway omits it). Mirrors CompareSize's
        // CompareBy shape — CompareBy + OrderByDescending = higher value sorts first. Tiebreaker
        // AFTER indexer priority (source stability) and BEFORE age (freshness) per user direction
        // (quick-260607-cto). Pre-wires the future highest-votes Custom Format idea without a re-plumb.
        // WR-01: null-safe — null Release sorts LAST (votes 0 worst sentinel).
        private int CompareVotes(MangaDownloadDecision x, MangaDownloadDecision y)
            => CompareBy(x.RemoteChapter?.Release, y.RemoteChapter?.Release, r => r?.Votes ?? 0);

        // Newer wins — later PublishDate = better. Mirrors TV CompareAgeIfUsenet shape with manga
        // simplification: no protocol gate (Phase 4 D-10 — manga is HTTP-only).
        //
        // DEBUG FIX (manga-search-sort-icomparer): compare the STABLE PublishDate field, NOT the
        // live ReleaseInfo.AgeHours getter. AgeHours recomputes DateTime.UtcNow on every read
        // (ReleaseInfo.cs:106-116), so two releases that share a PublishDate — the gateway stamps
        // DateTime.UtcNow on every dateless release (GatewayParser.cs:92), so a single search batch
        // collapses to near-identical dates — produce a non-zero, sign-unstable delta whose value
        // depends on which UtcNow read happened first. That makes Compare non-antisymmetric and
        // non-transitive, so OrderBy(d => d, _comparer) in ProcessMangaDownloadDecisions throws
        // "Unable to sort because the IComparer.Compare() method returns inconsistent results"
        // mid-sort (LINQ sorts an int[] index map, hence IComparer 'System.Comparison`1[System.Int32]').
        // PublishDate is a stored field, so comparing it is deterministic AND gives the IDENTICAL
        // ordering: AgeHours = UtcNow - PublishDate, so lower-age ⟺ later-PublishDate, which means
        // CompareByReverse(AgeHours) and CompareBy(PublishDate) yield the same sign for every input.
        // WR-01: null-safe — null Release sorts as oldest (DateTime.MinValue worst-age sentinel,
        // the PublishDate analog of the prior double.MaxValue age sentinel).
        private int CompareAge(MangaDownloadDecision x, MangaDownloadDecision y)
            => CompareBy(x.RemoteChapter?.Release, y.RemoteChapter?.Release, r => r?.PublishDate ?? DateTime.MinValue);

        // Larger size wins. Mirrors TV CompareSize fallback shape — CompareBy + OrderByDescending =
        // larger size sorts first.
        // WR-01: null-safe — null Release sorts LAST (size 0 worst-size sentinel).
        private int CompareSize(MangaDownloadDecision x, MangaDownloadDecision y)
            => CompareBy(x.RemoteChapter?.Release, y.RemoteChapter?.Release, r => r?.Size ?? 0L);
    }
}
