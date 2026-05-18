using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    // Phase 24 Plan 24-02 — verifies Migration 002 applies cleanly on a fresh DB
    // and adds Manga.Artist (TEXT NULL) + Manga.Demographic (INTEGER NULL) columns
    // (D-01 + D-04). Mirrors the SqliteOnly PRAGMA shape used by
    // 001_mangarr_baselineFixture (introspection via sqlite_master / PRAGMA).
    [TestFixture]
    [Category("SqliteOnly")]
    public class v1_1_manga_artist_demographicFixture : MigrationTest<v1_1_manga_artist_demographic>
    {
        // ============================================================
        // Test 1 — Migration 002 adds Manga.Artist (TEXT NULL).
        // ============================================================
        [Test]
        public void should_add_artist_column_to_manga_table()
        {
            var db = WithDapperMigrationTestDb();

            var rows = db.Query<TableInfoRow>("PRAGMA table_info(\"Manga\");").ToList();
            var artist = rows.SingleOrDefault(r => r.name == "Artist");

            artist.Should().NotBeNull("Manga.Artist column must exist after Migration 002 (Phase 24 D-03)");
            artist.notnull.Should().Be(0, "Manga.Artist must be nullable (string?)");
        }

        // ============================================================
        // Test 2 — Migration 002 adds Manga.Demographic (INTEGER NULL).
        // ============================================================
        [Test]
        public void should_add_demographic_column_to_manga_table()
        {
            var db = WithDapperMigrationTestDb();

            var rows = db.Query<TableInfoRow>("PRAGMA table_info(\"Manga\");").ToList();
            var demographic = rows.SingleOrDefault(r => r.name == "Demographic");

            demographic.Should().NotBeNull("Manga.Demographic column must exist after Migration 002 (Phase 24 D-04)");
            demographic.notnull.Should().Be(0, "Manga.Demographic must be nullable (MangaDemographic?)");
        }

        // ============================================================
        // Test 3 — Dapper int-enum round-trip for Demographic = Shonen (1).
        // ============================================================
        [Test]
        public void should_round_trip_demographic_enum_via_dapper()
        {
            var db = WithDapperMigrationTestDb();

            // Insert a minimal Manga row carrying Demographic = Shonen (1) + Artist = "Foo".
            // Manga table requires Title / CleanTitle / Status / Path / Monitored / Added (NotNullable
            // in baseline DDL).
            db.Execute(
                "INSERT INTO \"Manga\" " +
                "(\"Title\", \"CleanTitle\", \"Status\", \"Path\", \"Monitored\", \"Added\", \"Artist\", \"Demographic\") " +
                "VALUES (@Title, @CleanTitle, @Status, @Path, @Monitored, @Added, @Artist, @Demographic)",
                new
                {
                    Title = "Test Manga",
                    CleanTitle = "testmanga",
                    Status = "ongoing",
                    Path = "C:/manga/test",
                    Monitored = true,
                    Added = System.DateTime.UtcNow,
                    Artist = "Foo Artist",
                    Demographic = (int)MangaDemographic.Shonen,
                });

            var (gotArtist, gotDemographic) = db.Query<(string Artist, int? Demographic)>(
                "SELECT \"Artist\", \"Demographic\" FROM \"Manga\" WHERE \"Title\" = 'Test Manga'").Single();

            gotArtist.Should().Be("Foo Artist");
            gotDemographic.Should().Be((int)MangaDemographic.Shonen, "Demographic must round-trip as 1 (Shonen)");
        }

        // ============================================================
        // Helper POCO mirrors `PRAGMA table_info("X")` column shape exactly.
        // ============================================================
#pragma warning disable SA1300 // Element should begin with upper-case letter
        private class TableInfoRow
        {
            public int cid { get; set; }
            public string name { get; set; }
            public string type { get; set; }
            public int notnull { get; set; }
            public string dflt_value { get; set; }
            public int pk { get; set; }
        }
#pragma warning restore SA1300
    }
}
