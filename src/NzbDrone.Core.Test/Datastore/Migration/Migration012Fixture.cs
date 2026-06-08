using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    // Phase 41 — pins Migration 012 behavior:
    //   1. ADD the nullable Manga.MangaBakaId int column (D-03) — settable post-migration,
    //      NULL by default on a row that never sets it.
    //   2. DEMOTE the MangaDex-as-primary row only (D-01 guard):
    //      - when MangaDex IS the current primary, it is demoted to IsPrimary=0;
    //      - when a non-MangaDex provider is the explicit primary, the guard is a no-op (the
    //        explicit primary survives and MangaDex stays non-primary).
    //
    // Raw SQL uses double-quoted identifiers exactly as Migration011Fixture does (Postgres
    // case-preservation; SQLite-compatible). [Category("SqliteOnly")] mirrors the migration-
    // fixture convention.
    [TestFixture]
    [Category("SqliteOnly")]
    public class Migration012Fixture : MigrationTest<v1_3_add_mangabaka_metadata_source>
    {
        [Test]
        public void should_add_nullable_mangabakaid_column()
        {
            var db = WithDapperMigrationTestDb(beforeMigration: m =>
            {
                m.Execute.Sql(
                    "INSERT INTO \"Manga\" (\"Title\", \"CleanTitle\", \"Path\", \"Monitored\", \"Added\") " +
                    "VALUES ('Baka Manga', 'bakamanga', '/manga/baka', 1, '2026-01-01 00:00:00')");
                m.Execute.Sql(
                    "INSERT INTO \"Manga\" (\"Title\", \"CleanTitle\", \"Path\", \"Monitored\", \"Added\") " +
                    "VALUES ('Null Manga', 'nullmanga', '/manga/null', 1, '2026-01-01 00:00:00')");
            });

            // The column exists post-migration and is settable.
            db.Execute("UPDATE \"Manga\" SET \"MangaBakaId\" = 3397 WHERE \"Title\" = 'Baka Manga'");

            var set = db.Query<int?>("SELECT \"MangaBakaId\" FROM \"Manga\" WHERE \"Title\" = 'Baka Manga'").Single();
            var unset = db.Query<int?>("SELECT \"MangaBakaId\" FROM \"Manga\" WHERE \"Title\" = 'Null Manga'").Single();

            set.Should().Be(3397, "Migration 012 must add a settable nullable MangaBakaId int column");
            unset.Should().BeNull("a row that never sets MangaBakaId reads NULL");
        }

        [Test]
        public void should_demote_mangadex_when_it_is_the_current_primary()
        {
            var db = WithDapperMigrationTestDb(beforeMigration: m =>
            {
                m.Execute.Sql(
                    "INSERT INTO \"MetadataSources\" (\"Name\", \"Implementation\", \"Enable\", \"IsPrimary\") " +
                    "VALUES ('MangaDex', 'MangaDexMetadataSource', 1, 1)");
            });

            var isPrimary = db.Query<bool>(
                "SELECT \"IsPrimary\" FROM \"MetadataSources\" WHERE \"Implementation\" = 'MangaDexMetadataSource'").Single();

            isPrimary.Should().BeFalse("Migration 012 must demote MangaDex when it is the current primary");
        }

        [Test]
        public void should_leave_explicit_non_mangadex_primary_untouched()
        {
            var db = WithDapperMigrationTestDb(beforeMigration: m =>
            {
                m.Execute.Sql(
                    "INSERT INTO \"MetadataSources\" (\"Name\", \"Implementation\", \"Enable\", \"IsPrimary\") " +
                    "VALUES ('MangaDex', 'MangaDexMetadataSource', 1, 0)");
                m.Execute.Sql(
                    "INSERT INTO \"MetadataSources\" (\"Name\", \"Implementation\", \"Enable\", \"IsPrimary\") " +
                    "VALUES ('AniList', 'AniListMetadataSource', 1, 1)");
            });

            var aniListPrimary = db.Query<bool>(
                "SELECT \"IsPrimary\" FROM \"MetadataSources\" WHERE \"Implementation\" = 'AniListMetadataSource'").Single();
            var mangaDexPrimary = db.Query<bool>(
                "SELECT \"IsPrimary\" FROM \"MetadataSources\" WHERE \"Implementation\" = 'MangaDexMetadataSource'").Single();

            aniListPrimary.Should().BeTrue("the guard must leave an explicit non-MangaDex primary untouched");
            mangaDexPrimary.Should().BeFalse("MangaDex stays non-primary (guard no-op)");
        }
    }
}
