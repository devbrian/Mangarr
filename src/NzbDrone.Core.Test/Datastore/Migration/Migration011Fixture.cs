using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    // Issue #320 — pins Migration 011 behavior: the one-shot normalization of the
    // CustomFormatProfileId/TranslationProfileId == 0 sentinel persisted before the add-time
    // coercion fix (AddMangaService.PrepareForAdd). `0` is a non-existent FK (profile rows start
    // at id 1); the canonical "use the seeded default" sentinel is NULL (every consumer resolves
    // `Manga.<X>ProfileId ?? Config.Default<X>ProfileId`). The migration must:
    //   - set TranslationProfileId 0 -> NULL,
    //   - set CustomFormatProfileId 0 -> NULL,
    //   - leave real profile FKs (>= 1) untouched.
    [TestFixture]
    [Category("SqliteOnly")]
    public class Migration011Fixture : MigrationTest<normalize_manga_profile_ids>
    {
        [Test]
        public void should_normalize_zero_profile_ids_to_null()
        {
            var db = WithDapperMigrationTestDb(beforeMigration: m =>
            {
                m.Execute.Sql(
                    "INSERT INTO \"Manga\" (\"Title\", \"CleanTitle\", \"Path\", \"Monitored\", \"Added\", \"TranslationProfileId\", \"CustomFormatProfileId\") " +
                    "VALUES ('Zero Manga', 'zeromanga', '/manga/zero', 1, '2026-01-01 00:00:00', 0, 0)");
            });

            var tp = db.Query<int?>("SELECT \"TranslationProfileId\" FROM \"Manga\" WHERE \"Title\" = 'Zero Manga'").Single();
            var cf = db.Query<int?>("SELECT \"CustomFormatProfileId\" FROM \"Manga\" WHERE \"Title\" = 'Zero Manga'").Single();

            tp.Should().BeNull("Migration 011 must coerce TranslationProfileId 0 -> NULL");
            cf.Should().BeNull("Migration 011 must coerce CustomFormatProfileId 0 -> NULL");
        }

        [Test]
        public void should_leave_real_profile_ids_untouched()
        {
            var db = WithDapperMigrationTestDb(beforeMigration: m =>
            {
                m.Execute.Sql(
                    "INSERT INTO \"Manga\" (\"Title\", \"CleanTitle\", \"Path\", \"Monitored\", \"Added\", \"TranslationProfileId\", \"CustomFormatProfileId\") " +
                    "VALUES ('Real Manga', 'realmanga', '/manga/real', 1, '2026-01-01 00:00:00', 1, 2)");
            });

            var tp = db.Query<int?>("SELECT \"TranslationProfileId\" FROM \"Manga\" WHERE \"Title\" = 'Real Manga'").Single();
            var cf = db.Query<int?>("SELECT \"CustomFormatProfileId\" FROM \"Manga\" WHERE \"Title\" = 'Real Manga'").Single();

            tp.Should().Be(1, "a real profile FK must survive the normalization");
            cf.Should().Be(2, "a real profile FK must survive the normalization");
        }
    }
}
