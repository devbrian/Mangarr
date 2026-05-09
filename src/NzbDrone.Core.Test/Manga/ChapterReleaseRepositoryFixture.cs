// Sonarr divergence: NEW manga sibling test fixture per Phase 16 STRUCT-02 — see DIVERGENCE.md.
// Role-match analog: src/NzbDrone.Core.Test/Manga/ChapterRepositoryFixture.cs.
// Plan 16-02 (Wave 1) landed the production ChapterReleaseRepository + ChapterRelease
// types; the Wave-0 `#if PHASE_16_WAVE_1` guard was removed inline (Plan 16-01 hand-off
// option (b) — guard-removal vs. .csproj DefineConstants). The skeleton-method-name
// placeholder block is gone too; the live tests run unconditionally.
using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MangaTests
{
    [TestFixture]
    public class ChapterReleaseRepositoryFixture
        : DbTest<NzbDrone.Core.Manga.ChapterReleaseRepository, NzbDrone.Core.Manga.ChapterRelease>
    {
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
    }
}
