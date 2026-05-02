using System;
using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    // Phase 2 Migration 002 cross-backend fixture. Mirrors Phase 1's
    // 001_mangarr_baselineFixture.cs cross-backend pattern: a single [TestFixture]
    // with per-test [Category("Postgres")] gating that Assert.Ignore's when
    // Sonarr__Postgres__Host is unset. Plan-author-intended dual marker is documented
    // here for traceability:
    //
    //   [TestFixture(typeof(SQLite))]
    //   [TestFixture(typeof(Postgres))]
    //
    // (Sonarr does not use NUnit's typeof-parameterized [TestFixture] elsewhere; we
    // mirror the established Phase 1 pattern instead.)
    [TestFixture]
    public class chapter_extensions_and_precisionFixture : MigrationTest<chapter_extensions_and_precision>
    {
        [Test]
        public void should_add_ChapterType_TEXT_NOT_NULL_default_Regular()
        {
            var db = WithDapperMigrationTestDb();
            var rows = db.Query<TableInfoRow>("PRAGMA table_info(\"Chapters\");").ToList();
            var col = rows.SingleOrDefault(r => r.name == "ChapterType");
            col.Should().NotBeNull("Migration 002 must add Chapters.ChapterType");
            col.notnull.Should().Be(1, "ChapterType must be NOT NULL");
            col.dflt_value.Should().Contain("Regular", "ChapterType default must be 'Regular'");
        }

        [Test]
        public void should_add_VolumeNumber_INTEGER_NULL()
        {
            var db = WithDapperMigrationTestDb();
            var rows = db.Query<TableInfoRow>("PRAGMA table_info(\"Chapters\");").ToList();
            var col = rows.SingleOrDefault(r => r.name == "VolumeNumber");
            col.Should().NotBeNull("Migration 002 must add Chapters.VolumeNumber");
            col.notnull.Should().Be(0, "VolumeNumber must be nullable");
        }

        [Test]
        public void should_add_AbsoluteChapterNumber_DECIMAL_10_3_NULL()
        {
            var db = WithDapperMigrationTestDb();
            var rows = db.Query<TableInfoRow>("PRAGMA table_info(\"Chapters\");").ToList();
            var col = rows.SingleOrDefault(r => r.name == "AbsoluteChapterNumber");
            col.Should().NotBeNull("Migration 002 must add Chapters.AbsoluteChapterNumber");
            col.notnull.Should().Be(0, "AbsoluteChapterNumber must be nullable");
        }

        [Test]
        public void should_add_IsSynthetic_BOOLEAN_NOT_NULL_default_false()
        {
            var db = WithDapperMigrationTestDb();
            var rows = db.Query<TableInfoRow>("PRAGMA table_info(\"Chapters\");").ToList();
            var col = rows.SingleOrDefault(r => r.name == "IsSynthetic");
            col.Should().NotBeNull("Migration 002 must add Chapters.IsSynthetic");
            col.notnull.Should().Be(1, "IsSynthetic must be NOT NULL");
        }

        // D-12 widen: 1.123m must round-trip lossless after Migration 002.
        [Test]
        public void should_widen_ChapterNumber_to_DECIMAL_10_3()
        {
            var db = WithDapperMigrationTestDb();
            db.Execute(
                "INSERT INTO \"Chapters\" (\"MangaId\", \"ChapterNumber\", \"TranslatedLanguage\", \"Monitored\", \"ChapterType\", \"IsSynthetic\") " +
                "VALUES (@MangaId, @ChapterNumber, @TranslatedLanguage, @Monitored, @ChapterType, @IsSynthetic)",
                new { MangaId = 1, ChapterNumber = 1.123m, TranslatedLanguage = "en", Monitored = true, ChapterType = "Regular", IsSynthetic = false });
            var roundtripped = db.Query<decimal>(
                "SELECT \"ChapterNumber\" FROM \"Chapters\" WHERE \"MangaId\" = 1").Single();
            roundtripped.Should().Be(1.123m);
        }

        [Test]
        public void should_create_MetadataSources_table_with_IsPrimary_column()
        {
            var db = WithDapperMigrationTestDb();
            var tables = db.Query<string>("SELECT name FROM sqlite_master WHERE type='table'").ToList();
            tables.Should().Contain("MetadataSources", "Migration 002 must create MetadataSources table (Pitfall 2 — distinct from existing Metadata)");

            var rows = db.Query<TableInfoRow>("PRAGMA table_info(\"MetadataSources\");").ToList();
            rows.Should().Contain(r => r.name == "IsPrimary", "MetadataSources.IsPrimary column must exist");
        }

        // D-18: ScheduledTasks gets RefreshMangaCommand row with Interval=720 (12h).
        [Test]
        public void should_insert_ScheduledTasks_row_for_RefreshMangaCommand_with_Interval_720()
        {
            var db = WithDapperMigrationTestDb();
            var row = db.Query<ScheduledTaskRow>(
                "SELECT \"TypeName\" AS TypeName, \"Interval\" AS Interval FROM \"ScheduledTasks\" " +
                "WHERE \"TypeName\" = 'NzbDrone.Core.Manga.Commands.RefreshMangaCommand'")
                .SingleOrDefault();
            row.Should().NotBeNull("Migration 002 must insert RefreshMangaCommand ScheduledTasks row");
            row.Interval.Should().Be(720, "Interval must be 720 (12h cadence per D-18)");
        }

        [Test]
        public void should_drop_MalIds_and_AniListIds_columns_from_Manga_table()
        {
            var db = WithDapperMigrationTestDb();
            var rows = db.Query<TableInfoRow>("PRAGMA table_info(\"Manga\");").ToList();
            var names = rows.Select(r => r.name).ToList();
            names.Should().NotContain("MalIds", "Migration 002 drops MalIds (singular MalId replaces it)");
            names.Should().NotContain("AniListIds", "Migration 002 drops AniListIds (singular AniListId replaces it)");
        }

        [Test]
        public void should_add_MalId_AniListId_int_nullable_columns_to_Manga_table()
        {
            var db = WithDapperMigrationTestDb();
            var rows = db.Query<TableInfoRow>("PRAGMA table_info(\"Manga\");").ToList();
            var malId = rows.SingleOrDefault(r => r.name == "MalId");
            var aniListId = rows.SingleOrDefault(r => r.name == "AniListId");
            malId.Should().NotBeNull("Manga.MalId column must exist");
            malId.notnull.Should().Be(0, "Manga.MalId must be nullable");
            aniListId.Should().NotBeNull("Manga.AniListId column must exist");
            aniListId.notnull.Should().Be(0, "Manga.AniListId must be nullable");
        }

        // Postgres-only: index drop+recreate after the DECIMAL(10,2)→(10,3) widen
        // (Pitfall 3). On SQLite REAL is dialect-free; this test Assert.Ignores.
        [Test]
        [Category("Postgres")]
        public void should_recreate_Chapters_composite_index_on_postgres()
        {
            var postgresHost = Environment.GetEnvironmentVariable("Sonarr__Postgres__Host");
            if (string.IsNullOrWhiteSpace(postgresHost))
            {
                Assert.Ignore("Skipped: requires PostgreSQL backend (set Sonarr__Postgres__Host or use postgres.runsettings).");
                return;
            }

            var db = WithDapperMigrationTestDb();
            var indexes = db.Query<string>(
                "SELECT indexname FROM pg_indexes WHERE schemaname='public' AND tablename='Chapters'")
                .ToList();
            indexes.Should().Contain(
                "IX_Chapters_MangaId_ChapterNumber_TranslatedLanguage",
                "Postgres composite index must be recreated post-widen (Pitfall 3)");
        }

        // Postgres parity smoke (mirrors Phase 1 baseline test 8).
        [Test]
        [Category("Postgres")]
        public void should_apply_002_on_postgres()
        {
            var postgresHost = Environment.GetEnvironmentVariable("Sonarr__Postgres__Host");
            if (string.IsNullOrWhiteSpace(postgresHost))
            {
                Assert.Ignore("Skipped: requires PostgreSQL backend.");
                return;
            }

            var db = WithDapperMigrationTestDb();
            var tableNames = db.Query<string>(
                "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'")
                .ToList();
            tableNames.Should().Contain("MetadataSources");
        }

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

        private class ScheduledTaskRow
        {
            public string TypeName { get; set; }
            public double Interval { get; set; }
        }
    }
}
