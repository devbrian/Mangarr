// Sonarr divergence: NEW manga sibling test fixture per Phase 16 STRUCT-02 — see DIVERGENCE.md.
// Role-match analog: src/NzbDrone.Core.Test/Manga/ChapterRepositoryFixture.cs.
// Fixture is [Ignore]'d during Wave 0; Plan 16-02 (Wave 1) lands the ChapterReleaseRepository
// + ChapterRelease types this fixture exercises, at which point the [Ignore] is removed.
//
// Wave 0 deviation (Rule 3 — Blocking issue): the plan body asserts that this fixture
// "compiles" pre-Wave-1, but the DbTest<TRepo, TEntity> base requires both type arguments
// to exist at compile time — Moq mocks alone cannot satisfy the open generic constraint.
// The fixture body is therefore wrapped in `#if PHASE_16_WAVE_1` (symbol intentionally
// undefined in the .csproj). Plan 16-02 (Wave 1) lands ChapterReleaseRepository +
// ChapterRelease, then either (a) removes the `#if` guard outright or (b) defines
// PHASE_16_WAVE_1 in the .csproj. Either path turns this fixture GREEN. The class shell
// + [Ignore] markers are PRESERVED outside the guard so Sonarr-divergence and Wave-1
// dependency markers remain greppable per acceptance criteria.
#if PHASE_16_WAVE_1
using System;
using System.Linq;
using FluentAssertions;
using NzbDrone.Core.Test.Framework;
#endif
using NUnit.Framework;

namespace NzbDrone.Core.Test.MangaTests
{
    [TestFixture]
    [Ignore("Wave 1 dependency: ChapterReleaseRepository + ChapterRelease created in Plan 16-02")]
    public class ChapterReleaseRepositoryFixture
#if PHASE_16_WAVE_1
        : DbTest<NzbDrone.Core.Manga.ChapterReleaseRepository, NzbDrone.Core.Manga.ChapterRelease>
#endif
    {
#if PHASE_16_WAVE_1
        private NzbDrone.Core.Manga.ChapterRelease BuildRelease(int chapterId = 1, string lang = "en", string group = "MangaPlus")
        {
            return new NzbDrone.Core.Manga.ChapterRelease
            {
                ChapterId = chapterId,
                TranslatedLanguage = lang,
                ScanlationGroup = group,
                ReleaseDate = System.DateTime.UtcNow,
                ExternalId = System.Guid.NewGuid().ToString(),
            };
        }

        [Test]
        public void Insert_persists_release_and_round_trips_natural_key()
        {
            var release = BuildRelease(chapterId: 7, lang: "en", group: "MangaPlus");
            Subject.Insert(release);

            var fetched = Subject.Get(release.Id);
            fetched.ChapterId.Should().Be(7);
            fetched.TranslatedLanguage.Should().Be("en");
            fetched.ScanlationGroup.Should().Be("MangaPlus");
        }

        [Test]
        public void Find_by_natural_key_returns_match()
        {
            Subject.Insert(BuildRelease(chapterId: 1, lang: "en", group: "MangaPlus"));
            Subject.Insert(BuildRelease(chapterId: 1, lang: "es", group: "MangaPlus"));
            Subject.Insert(BuildRelease(chapterId: 2, lang: "en", group: "MangaPlus"));

            var found = Subject.Find(1, "es", "MangaPlus");

            found.Should().NotBeNull();
            found.ChapterId.Should().Be(1);
            found.TranslatedLanguage.Should().Be("es");
        }

        [Test]
        public void Insert_duplicate_natural_key_throws_unique_violation()
        {
            Subject.Insert(BuildRelease(chapterId: 1, lang: "en", group: "MangaPlus"));

            Action act = () => Subject.Insert(BuildRelease(chapterId: 1, lang: "en", group: "MangaPlus"));

            // Wildcard match — works on both SQLiteException ("UNIQUE constraint failed: ...")
            // and PostgresException ("duplicate key value violates unique constraint ...") per Pitfall 7.
            act.Should().Throw<Exception>().Where(e =>
                e.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase));
        }
#else
        // Wave 0 placeholder: skeleton method names PRESERVED for Sonarr-consistency-audit
        // greppability (acceptance criteria checks for these literal method names). Each is
        // a no-op that throws NotImplementedException — but the [Ignore] at the class level
        // means NUnit never invokes them, so the suite stays GREEN.

        [Test]
        public void Insert_persists_release_and_round_trips_natural_key()
        {
            throw new System.NotImplementedException("Wave 1 stub — Plan 16-02 lands real implementation");
        }

        [Test]
        public void Find_by_natural_key_returns_match()
        {
            throw new System.NotImplementedException("Wave 1 stub — Plan 16-02 lands real implementation");
        }

        [Test]
        public void Insert_duplicate_natural_key_throws_unique_violation()
        {
            throw new System.NotImplementedException("Wave 1 stub — Plan 16-02 lands real implementation");
        }
#endif
    }
}
