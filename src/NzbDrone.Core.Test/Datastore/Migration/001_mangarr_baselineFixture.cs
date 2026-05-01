using System.Collections.Generic;
using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class mangarr_baselineFixture : MigrationTest<mangarr_baseline>
    {
        // ============================================================
        // Test 1 — Manga table created with the v1 column shape (D-08)
        // ============================================================
        [Test]
        public void should_create_manga_table_with_correct_columns()
        {
            var db = WithDapperMigrationTestDb();

            var rows = db.Query<TableInfoRow>("PRAGMA table_info(\"Manga\");");

            var columnNames = rows.Select(r => r.name).ToList();

            columnNames.Should().Contain("Id");
            columnNames.Should().Contain("Title");
            columnNames.Should().Contain("CleanTitle");
            columnNames.Should().Contain("MangaDexId");
            columnNames.Should().Contain("MalIds");
            columnNames.Should().Contain("AniListIds");
            columnNames.Should().Contain("Path");
            columnNames.Should().Contain("Monitored");
            columnNames.Should().Contain("Status");
            columnNames.Should().Contain("Added");
            columnNames.Should().Contain("LastInfoSync");
            columnNames.Should().Contain("Images");
            columnNames.Should().Contain("Tags");
        }

        // ============================================================
        // Test 2 — ChapterNumber DECIMAL(10,2) round-trips lossless (D-09)
        // ============================================================
        [Test]
        public void should_create_chapters_table_with_decimal_chapter_number()
        {
            var db = WithDapperMigrationTestDb();

            db.Execute(
                "INSERT INTO \"Chapters\" (\"MangaId\", \"ChapterNumber\", \"TranslatedLanguage\", \"Monitored\") " +
                "VALUES (@MangaId, @ChapterNumber, @TranslatedLanguage, @Monitored)",
                new { MangaId = 1, ChapterNumber = 123.95m, TranslatedLanguage = "en", Monitored = true });

            var roundtripped = db.Query<decimal>(
                "SELECT \"ChapterNumber\" FROM \"Chapters\" WHERE \"MangaId\" = 1").Single();

            roundtripped.Should().Be(123.95m);
        }

        // ============================================================
        // Test 3 — History gets nullable MangaId / ChapterId columns (D-07)
        // ============================================================
        [Test]
        public void should_create_history_with_nullable_manga_columns()
        {
            var db = WithDapperMigrationTestDb();

            var rows = db.Query<TableInfoRow>("PRAGMA table_info(\"History\");").ToList();

            var mangaIdCol = rows.SingleOrDefault(r => r.name == "MangaId");
            var chapterIdCol = rows.SingleOrDefault(r => r.name == "ChapterId");

            mangaIdCol.Should().NotBeNull("History.MangaId column must exist for D-07");
            mangaIdCol.notnull.Should().Be(0, "History.MangaId must be nullable");

            chapterIdCol.Should().NotBeNull("History.ChapterId column must exist for D-07");
            chapterIdCol.notnull.Should().Be(0, "History.ChapterId must be nullable");
        }

        // ============================================================
        // Test 4 — Blocklist gets nullable MangaId column (D-07)
        // ============================================================
        [Test]
        public void should_create_blocklist_with_nullable_mangaid()
        {
            var db = WithDapperMigrationTestDb();

            var rows = db.Query<TableInfoRow>("PRAGMA table_info(\"Blocklist\");").ToList();

            var mangaIdCol = rows.SingleOrDefault(r => r.name == "MangaId");

            mangaIdCol.Should().NotBeNull("Blocklist.MangaId column must exist for D-07");
            mangaIdCol.notnull.Should().Be(0, "Blocklist.MangaId must be nullable");
        }

        // ============================================================
        // Test 5 — Five locked composite indexes from Phase 0 D-15 ship Day 1
        // ============================================================
        [Test]
        public void should_create_five_locked_composite_indexes()
        {
            var db = WithDapperMigrationTestDb();

            var indexes = db.Query<IndexInfo>(
                "SELECT name, tbl_name AS TableName, sql FROM sqlite_master WHERE type='index' AND sql IS NOT NULL")
                .ToList();

            // 1. (History.MangaId, Date DESC)
            indexes.Should().Contain(
                i => i.TableName == "History" &&
                     System.Text.RegularExpressions.Regex.IsMatch(i.Sql ?? string.Empty, "MangaId.*Date") &&
                     !System.Text.RegularExpressions.Regex.IsMatch(i.Sql ?? string.Empty, "MangaId.*ChapterId.*Date"),
                "(History.MangaId, Date DESC) index must exist");

            // 2. (History.MangaId, ChapterId, Date DESC)
            indexes.Should().Contain(
                i => i.TableName == "History" &&
                     System.Text.RegularExpressions.Regex.IsMatch(i.Sql ?? string.Empty, "MangaId.*ChapterId.*Date"),
                "(History.MangaId, ChapterId, Date DESC) index must exist");

            // 3. (Blocklist.MangaId, Date DESC)
            indexes.Should().Contain(
                i => i.TableName == "Blocklist" &&
                     System.Text.RegularExpressions.Regex.IsMatch(i.Sql ?? string.Empty, "MangaId.*Date"),
                "(Blocklist.MangaId, Date DESC) index must exist");

            // 4. (Chapters.MangaId, ChapterNumber, TranslatedLanguage)
            indexes.Should().Contain(
                i => i.TableName == "Chapters" &&
                     System.Text.RegularExpressions.Regex.IsMatch(
                         i.Sql ?? string.Empty, "MangaId.*ChapterNumber.*TranslatedLanguage"),
                "(Chapters.MangaId, ChapterNumber, TranslatedLanguage) index must exist");
        }

        // ============================================================
        // Test 6 — Series/Seasons/Episodes/EpisodeFiles recreated for compile compat (D-02)
        // ============================================================
        [Test]
        public void should_recreate_tv_tables_for_compile_compat()
        {
            var db = WithDapperMigrationTestDb();

            var tableNames = db.Query<string>(
                "SELECT name FROM sqlite_master WHERE type='table'").ToList();

            tableNames.Should().Contain("Series", "Series table must be recreated per D-02");
            tableNames.Should().Contain("Seasons", "Seasons table must be recreated per D-02");
            tableNames.Should().Contain("Episodes", "Episodes table must be recreated per D-02");
            tableNames.Should().Contain("EpisodeFiles", "EpisodeFiles table must be recreated per D-02");
        }

        // ============================================================
        // Test 7 — ThingiProvider tables recreated verbatim (D-03)
        // ============================================================
        [Test]
        public void should_recreate_thingiprovider_tables()
        {
            var db = WithDapperMigrationTestDb();

            var tableNames = db.Query<string>(
                "SELECT name FROM sqlite_master WHERE type='table'").ToList();

            tableNames.Should().Contain("Indexers");
            tableNames.Should().Contain("DownloadClients");
            tableNames.Should().Contain("Notifications");
            tableNames.Should().Contain("Metadata");
            tableNames.Should().Contain("Tags");
            tableNames.Should().Contain("Config");
            tableNames.Should().Contain("RootFolders");
            tableNames.Should().Contain("NamingConfig");
        }

        // ============================================================
        // Test 8 — Postgres parity (DB-02). Tagged so the Postgres parity
        // assertions only run when env vars target a real Postgres instance
        // (postgres.runsettings or equivalent — see plan task 3 human-verify
        // checkpoint). On SQLite the test is Ignored so the suite stays green
        // for default local runs.
        // ============================================================
        [Test]
        [Category("Postgres")]
        public void should_apply_baseline_on_postgres()
        {
            var postgresHost = System.Environment.GetEnvironmentVariable("Sonarr__Postgres__Host");
            if (string.IsNullOrWhiteSpace(postgresHost))
            {
                Assert.Ignore("Skipped: requires PostgreSQL backend (set Sonarr__Postgres__Host env var or use postgres.runsettings).");
                return;
            }

            var db = WithDapperMigrationTestDb();

            // On Postgres backend, query information_schema instead of sqlite_master.
            // This same test, run with postgres.runsettings, exercises the Postgres-specific
            // DDL pathway for the entire baseline migration.
            var tableNames = db.Query<string>(
                "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'")
                .ToList();

            tableNames.Should().Contain("Manga");
            tableNames.Should().Contain("Chapters");
            tableNames.Should().Contain("Series");
            tableNames.Should().Contain("Indexers");
        }

        // ============================================================
        // Test 9 — (Chapters.MangaId, TranslatedLanguage) two-column index distinct
        // from the three-column index (D-15 leftmost-prefix proof). See Pitfall 3 in
        // 01-RESEARCH.md — this index cannot be served by the leftmost-prefix of #4
        // because ChapterNumber sits between MangaId and TranslatedLanguage.
        // ============================================================
        [Test]
        public void should_create_chapters_translatedlanguage_index_path()
        {
            var db = WithDapperMigrationTestDb();

            var chapterIndexes = db.Query<IndexInfo>(
                "SELECT name, tbl_name AS TableName, sql FROM sqlite_master " +
                "WHERE type='index' AND tbl_name='Chapters' AND sql IS NOT NULL")
                .ToList();

            // The two-column (MangaId, TranslatedLanguage) index must exist as its own index
            // — distinct from the three-column index that includes ChapterNumber between them.
            chapterIndexes.Should().Contain(
                i => System.Text.RegularExpressions.Regex.IsMatch(
                         i.Sql ?? string.Empty, "MangaId.*TranslatedLanguage") &&
                     !System.Text.RegularExpressions.Regex.IsMatch(
                         i.Sql ?? string.Empty, "ChapterNumber"),
                "(Chapters.MangaId, TranslatedLanguage) two-column index must exist on its own; "
                + "leftmost-prefix from the three-column index does NOT cover this path "
                + "because ChapterNumber sits between the two columns.");
        }

        // ============================================================
        // Helper POCOs
        // ============================================================
        // Mirrors `PRAGMA table_info("X")` column shape exactly. Lowercase
        // property names match SQLite's column names so Dapper binds without
        // alias gymnastics.
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

        private class IndexInfo
        {
            public string Name { get; set; }
            public string TableName { get; set; }
            public string Sql { get; set; }
        }
    }
}
