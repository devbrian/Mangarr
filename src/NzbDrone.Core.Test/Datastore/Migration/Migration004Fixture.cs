using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    // Phase 30 Plan 30-05 (II2-03) — verifies Migration 004 applies cleanly on a fresh DB
    // and produces the post-cluster schema shape:
    //   - II2-03: ChapterFile.MediaInfo TEXT NULL column added
    //   - II2-02: Conditional ComicInfoMetadata seed row IFF Config.metadataformats
    //             contains "comicinfo" (D-03 preserves user state from default ["comicinfo"])
    //   - R-10:   No Create.Table("Metadata") — table already exists in 001_mangarr_baseline.cs
    //
    // beforeMigration callback runs INSIDE the migration's Up() via NzbDroneMigrationBase
    // (line 36-39) — before MainDbUpgrade() runs, so any Execute statements emitted here
    // share the migration TX with Migration 004 itself. This is the canonical pattern for
    // seeding Config rows pre-migration so the conditional INSERT path can branch on user state.
    //
    // Mirrors the SqliteOnly PRAGMA + sqlite_master introspection shape established by
    // 001_mangarr_baselineFixture + 002_v1_1_manga_artist_demographicFixture + Migration003Fixture.
    [TestFixture]
    [Category("SqliteOnly")]
    public class Migration004Fixture : MigrationTest<chapterfile_mediainfo_metadata_seed>
    {
        // ============================================================
        // Test 1 — II2-03: ChapterFiles.MediaInfo TEXT NULL column added
        // ============================================================
        [Test]
        public void should_add_mediainfo_column_to_chapterfiles_table()
        {
            var db = WithDapperMigrationTestDb();

            var rows = db.Query<TableInfoRow>("PRAGMA table_info(\"ChapterFiles\");").ToList();
            var mediaInfo = rows.SingleOrDefault(r => r.name == "MediaInfo");

            mediaInfo.Should().NotBeNull("ChapterFiles.MediaInfo column must exist after Migration 004 (Plan 30-05 II2-03)");
            mediaInfo.notnull.Should().Be(0, "ChapterFiles.MediaInfo must be nullable (ChapterMediaInfo? — pre-Migration-004 rows stay NULL per D-05)");
        }

        // ============================================================
        // Test 2 — II2-02: ComicInfoMetadata row seeded when Config.metadataformats
        //          contains "comicinfo" (default state per Phase 4 D-14)
        // ============================================================
        [Test]
        public void should_seed_comicinfometadata_when_config_has_comicinfo()
        {
            var db = WithDapperMigrationTestDb(beforeMigration: m =>
            {
                m.Execute.Sql(
                    "INSERT INTO \"Config\" (\"Key\", \"Value\") VALUES ('metadataformats', '[\"comicinfo\"]')");
            });

            var count = db.Query<int>(
                "SELECT COUNT(*) FROM \"Metadata\" WHERE \"Implementation\" = 'ComicInfoMetadata' AND \"Enable\" = 1").Single();
            count.Should().Be(1, "Migration 004 must seed an enabled ComicInfoMetadata MetadataDefinition row when Config.metadataformats contains 'comicinfo' (D-03 user-state preservation)");
        }

        // ============================================================
        // Test 3 — II2-02: NO seed when Config.metadataformats is "[]"
        //          (user explicitly disabled ComicInfo writers pre-migration)
        // ============================================================
        [Test]
        public void should_not_seed_when_config_metadataformats_empty()
        {
            var db = WithDapperMigrationTestDb(beforeMigration: m =>
            {
                m.Execute.Sql(
                    "INSERT INTO \"Config\" (\"Key\", \"Value\") VALUES ('metadataformats', '[]')");
            });

            var count = db.Query<int>(
                "SELECT COUNT(*) FROM \"Metadata\" WHERE \"Implementation\" = 'ComicInfoMetadata'").Single();
            count.Should().Be(0, "Migration 004 must NOT seed ComicInfoMetadata when Config.metadataformats is empty (D-03 user-state preservation)");
        }

        // ============================================================
        // Test 4 — II2-02: ComicInfoMetadata seeded when Config row ABSENT
        //          (fresh-install default per ConfigService.MetadataFormats getter)
        // ============================================================
        [Test]
        public void should_seed_comicinfometadata_when_config_row_absent()
        {
            // Config.metadataformats row not pre-seeded; Migration 004 falls back to default ["comicinfo"].
            var db = WithDapperMigrationTestDb();

            var count = db.Query<int>(
                "SELECT COUNT(*) FROM \"Metadata\" WHERE \"Implementation\" = 'ComicInfoMetadata' AND \"Enable\" = 1").Single();
            count.Should().Be(1, "Migration 004 must seed ComicInfoMetadata when Config.metadataformats row is absent (default per ConfigService.MetadataFormats = [\"comicinfo\"])");
        }

        // ============================================================
        // Test 5 — R-10 verification: Migration 004 does NOT create the Metadata table.
        //          Confirmed by the migration source containing no Create.Table call;
        //          the table already exists in baseline.
        // ============================================================
        [Test]
        public void should_not_create_metadata_table_table_pre_exists_in_baseline()
        {
            var db = WithDapperMigrationTestDb();

            // Table exists (from baseline) — Migration 004 only INSERTs into it.
            var tableCount = db.Query<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Metadata'").Single();
            tableCount.Should().Be(1, "Metadata table pre-exists in 001_mangarr_baseline.cs:129-134 (R-10 — Plan 30-05 ALTER + INSERT only)");

            // Columns match baseline shape (5 cols: Enable, Name, Implementation, Settings, ConfigContract — no Tags).
            var cols = db.Query<TableInfoRow>("PRAGMA table_info(\"Metadata\");")
                         .Select(r => r.name)
                         .ToList();
            cols.Should().Contain("Enable");
            cols.Should().Contain("Name");
            cols.Should().Contain("Implementation");
            cols.Should().Contain("Settings");
            cols.Should().Contain("ConfigContract");
            cols.Should().NotContain("Tags", "R-11 mitigation — baseline Metadata table has no Tags column; Migration 004 INSERT supplies only the 5 baseline columns");
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
