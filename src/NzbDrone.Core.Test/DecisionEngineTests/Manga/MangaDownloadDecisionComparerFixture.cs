using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine.Manga;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Profiles.Translations;

namespace NzbDrone.Core.Test.DecisionEngineTests.Manga
{
    // Test semantics note: callers consume the comparer via OrderByDescending (mirror Mangarr's
    // DownloadDecisionPriorizationService.cs:29). With OrderByDescending(comparer), the item
    // with the HIGHER Compare value sorts FIRST. So "X wins" means Subject.Compare(loser, winner)
    // returns a NEGATIVE value (loser < winner) — winner sorts first under OrderByDescending.
    [TestFixture]
    public class MangaDownloadDecisionComparerFixture
        : MangaDecisionEngineSpecFixtureBase<MangaDownloadDecisionComparer>
    {
        private void GivenProfile(int id, params string[] languages)
        {
            Mocker.GetMock<ITranslationProfileService>()
                  .Setup(s => s.Get(id))
                  .Returns(BuildTranslationProfile(id, languages));
        }

        private MangaDownloadDecision Decision(RemoteChapter rc) => new MangaDownloadDecision(rc);

        [Test]
        public void ranks_language_before_cf_score()
        {
            // Profile prefers en (rank 0), then es (rank 1). A is en, B is es — A wins
            // despite B having higher CF score (language is the outer gate per D-08).
            GivenProfile(7, "en", "es");
            var a = Decision(BuildRemoteChapter(releaseLanguage: "en", customFormatScore: 10, translationProfileId: 7));
            var b = Decision(BuildRemoteChapter(releaseLanguage: "es", customFormatScore: 100, translationProfileId: 7));

            // A wins: Compare(loser=B, winner=A) < 0 ; equivalently Compare(A, B) > 0.
            Subject.Compare(a, b).Should().BeGreaterThan(0);
            Subject.Compare(b, a).Should().BeLessThan(0);
        }

        [Test]
        public void within_same_language_rank_higher_cf_score_wins()
        {
            GivenProfile(7, "en");
            var a = Decision(BuildRemoteChapter(releaseLanguage: "en", customFormatScore: 50, translationProfileId: 7));
            var b = Decision(BuildRemoteChapter(releaseLanguage: "en", customFormatScore: 100, translationProfileId: 7));

            // B wins (higher CF score): Compare(loser=A, winner=B) < 0.
            Subject.Compare(a, b).Should().BeLessThan(0);
            Subject.Compare(b, a).Should().BeGreaterThan(0);
        }

        [Test]
        public void within_same_language_and_cf_lower_indexer_priority_wins()
        {
            GivenProfile(7, "en");
            var a = Decision(BuildRemoteChapter(releaseLanguage: "en", customFormatScore: 50, translationProfileId: 7, indexerPriority: 10));
            var b = Decision(BuildRemoteChapter(releaseLanguage: "en", customFormatScore: 50, translationProfileId: 7, indexerPriority: 5));

            // B wins (lower IndexerPriority value = better — mirrors TV CompareIndexerPriority).
            Subject.Compare(a, b).Should().BeLessThan(0);
            Subject.Compare(b, a).Should().BeGreaterThan(0);
        }

        [Test]
        public void language_not_in_profile_sorts_last()
        {
            GivenProfile(7, "en");
            var a = Decision(BuildRemoteChapter(releaseLanguage: "en", customFormatScore: 10, translationProfileId: 7));
            var b = Decision(BuildRemoteChapter(releaseLanguage: "ko", customFormatScore: 999, translationProfileId: 7));

            // A wins despite lower CF — B's language is not in the profile (rank=int.MaxValue).
            Subject.Compare(a, b).Should().BeGreaterThan(0);
            Subject.Compare(b, a).Should().BeLessThan(0);
        }

        // Phase 16.1 Wave 3 (REVERT-03): this comparer already operated at release-grain
        // (`rc?.Release?.TranslatedLanguage` — the indexer-time projection on the
        // RemoteChapter). The Phase 16 persistent layer was reverted, but this comparer
        // was a verify-only no-op throughout (Pitfall #3 in the Phase 16.1 PATTERNS.md).
        // Tests below are the structural reflection-guard + a multi-candidate language
        // ranking case driven purely by RemoteChapter.Release.TranslatedLanguage. Sibling
        // guard in LanguageInTranslationProfileSpecificationFixture mirrors this shape.

        [Test]
        public void Comparer_does_not_reference_Chapter_TranslatedLanguage()
        {
            // Living-documentation guard: sibling of LanguageInTranslationProfileSpecification's guard.
            typeof(NzbDrone.Core.Manga.Chapter)
                .GetProperty("TranslatedLanguage").Should().BeNull();
        }

        [Test]
        public void Multi_release_ranking_picks_higher_priority_language()
        {
            // The comparer ranks two RemoteChapter candidates for the same canonical chapter,
            // each carrying a different RemoteChapter.Release.TranslatedLanguage (en vs es).
            // Profile prefers en (rank 0), then es (rank 1). The en candidate must rank ahead
            // of the es candidate under OrderByDescending (Subject.Compare(loser=es, winner=en) < 0).
            GivenProfile(7, "en", "es");
            var enCandidate = Decision(BuildRemoteChapter(releaseLanguage: "en", customFormatScore: 0, translationProfileId: 7));
            var esCandidate = Decision(BuildRemoteChapter(releaseLanguage: "es", customFormatScore: 0, translationProfileId: 7));

            // en wins: Compare(loser=es, winner=en) < 0 ; equivalently Compare(en, es) > 0.
            Subject.Compare(enCandidate, esCandidate).Should().BeGreaterThan(0);
            Subject.Compare(esCandidate, enCandidate).Should().BeLessThan(0);
        }
    }
}
