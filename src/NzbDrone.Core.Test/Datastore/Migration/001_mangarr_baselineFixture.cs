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
            columnNames.Should().Contain("MalId");
            columnNames.Should().Contain("AniListId");
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
        // Phase 16 STRUCT-01: TranslatedLanguage column dropped from Chapters (canonical
        // (MangaId, ChapterNumber) grain). Phase 16.1 keeps that shape; per-translation
        // language lives on ChapterFile post-import (D-06).
        // ============================================================
        [Test]
        public void should_create_chapters_table_with_decimal_chapter_number()
        {
            var db = WithDapperMigrationTestDb();

            db.Execute(
                "INSERT INTO \"Chapters\" (\"MangaId\", \"ChapterNumber\", \"Monitored\") " +
                "VALUES (@MangaId, @ChapterNumber, @Monitored)",
                new { MangaId = 1, ChapterNumber = 123.95m, Monitored = true });

            var roundtripped = db.Query<decimal>(
                "SELECT \"ChapterNumber\" FROM \"Chapters\" WHERE \"MangaId\" = 1").Single();

            roundtripped.Should().Be(123.95m);
        }

        // ============================================================
        // Test 3 — History gets NotNullable MangaId / ChapterId columns.
        // Phase 1 D-07 originally specified nullable; Phase 15 D-23 flipped both
        // to NotNullable() once the TV codepaths were deleted ("every row IS manga
        // post-Phase-15; NULL becomes meaningless"). See 001_mangarr_baseline.cs:541-552
        // and commit 5a545156f.
        // ============================================================
        [Test]
        public void should_create_history_with_notnullable_manga_columns()
        {
            var db = WithDapperMigrationTestDb();

            var rows = db.Query<TableInfoRow>("PRAGMA table_info(\"History\");").ToList();

            var mangaIdCol = rows.SingleOrDefault(r => r.name == "MangaId");
            var chapterIdCol = rows.SingleOrDefault(r => r.name == "ChapterId");

            mangaIdCol.Should().NotBeNull("History.MangaId column must exist (Phase 15 D-23)");
            mangaIdCol.notnull.Should().Be(1, "History.MangaId must be NOT NULL per Phase 15 D-23 (every row IS manga post-Phase-15)");

            chapterIdCol.Should().NotBeNull("History.ChapterId column must exist (Phase 15 D-23)");
            chapterIdCol.notnull.Should().Be(1, "History.ChapterId must be NOT NULL per Phase 15 D-23");
        }

        // ============================================================
        // Test 4 — Blocklist gets NotNullable MangaId column.
        // Phase 1 D-07 originally specified nullable; Phase 15 D-23 flipped to
        // NotNullable() (and added Blocklist.ChapterId NotNullable in the same pass).
        // See 001_mangarr_baseline.cs:541-552 and commit 5a545156f.
        // ============================================================
        [Test]
        public void should_create_blocklist_with_notnullable_mangaid()
        {
            var db = WithDapperMigrationTestDb();

            var rows = db.Query<TableInfoRow>("PRAGMA table_info(\"Blocklist\");").ToList();

            var mangaIdCol = rows.SingleOrDefault(r => r.name == "MangaId");
            var chapterIdCol = rows.SingleOrDefault(r => r.name == "ChapterId");

            mangaIdCol.Should().NotBeNull("Blocklist.MangaId column must exist (Phase 15 D-23)");
            mangaIdCol.notnull.Should().Be(1, "Blocklist.MangaId must be NOT NULL per Phase 15 D-23 (every row IS manga post-Phase-15)");

            chapterIdCol.Should().NotBeNull("Blocklist.ChapterId column must exist (Phase 15 D-23 — added in same pass as NotNullable flip)");
            chapterIdCol.notnull.Should().Be(1, "Blocklist.ChapterId must be NOT NULL per Phase 15 D-23");
        }

        // ============================================================
        // Test 5 — Phase 0 D-15 composite indexes — updated for Phase 16 STRUCT-01.
        // Indexes #4 + #5 (the language-keyed Chapters indexes) were replaced by the
        // composite UNIQUE on (Chapters.MangaId, ChapterNumber); the (MangaId, ChapterNumber, TranslatedLanguage)
        // 3-col index and the (MangaId, TranslatedLanguage) 2-col index are gone — see
        // Phase 16 STRUCT-01 + Plan 16-02 schema cutover.
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

            // 4. Phase 16 STRUCT-01 — composite UNIQUE on (Chapters.MangaId, ChapterNumber).
            //    Replaces the pre-Phase-16 (MangaId, ChapterNumber, TranslatedLanguage) 3-col +
            //    (MangaId, TranslatedLanguage) 2-col language-keyed indexes.
            indexes.Should().Contain(
                i => i.Name == "IX_Chapters_MangaId_ChapterNumber" && i.TableName == "Chapters",
                "(Chapters.MangaId, ChapterNumber) composite UNIQUE must exist (Phase 16 STRUCT-01)");
        }

        // ============================================================
        // Test 6 — TV-domain tables MUST NOT exist post-Phase-15 D-22.
        // Phase 1 D-02 originally recreated Series/Seasons/Episodes/EpisodeFiles as
        // empty compile-compat shims; Phase 15 D-22 dropped them entirely once the Tv/
        // C# subtree was deleted (Plan 15-03). The baseline must not resurrect them.
        // See 001_mangarr_baseline.cs:199-203 and commit 5a545156f.
        // ============================================================
        [Test]
        public void should_not_create_tv_tables_per_phase_15_d22()
        {
            var db = WithDapperMigrationTestDb();

            var tableNames = db.Query<string>(
                "SELECT name FROM sqlite_master WHERE type='table'").ToList();

            tableNames.Should().NotContain("Series", "Series table dropped per Phase 15 D-22 (TV-only; manga uses Manga table)");
            tableNames.Should().NotContain("Seasons", "Seasons table dropped per Phase 15 D-22 (manga has no season concept per DOMAIN-02)");
            tableNames.Should().NotContain("Episodes", "Episodes table dropped per Phase 15 D-22 (manga uses Chapters table)");
            tableNames.Should().NotContain("EpisodeFiles", "EpisodeFiles table dropped per Phase 15 D-22 (manga uses ChapterFiles table)");
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

            // Phase 15 D-22 — Series table dropped; assert manga peer + ThingiProvider sentinel instead.
            tableNames.Should().Contain("ChapterFiles");
            tableNames.Should().Contain("Indexers");
        }

        // ============================================================
        // Test 9 — Phase 16 STRUCT-01: language-keyed Chapters indexes are GONE.
        // The pre-Phase-16 (MangaId, TranslatedLanguage) two-column index and the
        // (MangaId, ChapterNumber, TranslatedLanguage) three-column index were both
        // dropped when Plan 16-02 lifted the language axis off the canonical Chapter
        // grain. Phase 16.1 keeps that canonical (MangaId, ChapterNumber) UNIQUE shape;
        // per-language data lives on ChapterFile (TranslatedLanguage + ScanlationGroup)
        // post-import — see should_create_chapter_files_table_round_trip below.
        // ============================================================
        [Test]
        public void should_drop_pre_phase_16_translatedlanguage_chapters_indexes()
        {
            var db = WithDapperMigrationTestDb();

            var chapterIndexes = db.Query<IndexInfo>(
                "SELECT name, tbl_name AS TableName, sql FROM sqlite_master " +
                "WHERE type='index' AND tbl_name='Chapters' AND sql IS NOT NULL")
                .ToList();

            chapterIndexes.Should().NotContain(
                i => System.Text.RegularExpressions.Regex.IsMatch(
                         i.Sql ?? string.Empty, "TranslatedLanguage"),
                "language-keyed Chapters indexes were dropped in Phase 16 STRUCT-01 / Plan 16-02");
        }

        // ============================================================
        // Phase 16.1 REVERT-01 + REVERT-02 — ChapterReleases table + 2 indexes
        // are GONE. Per-translation language and packager axes live on ChapterFile
        // (TranslatedLanguage + ScanlationGroup) instead — see
        // should_create_chapter_files_table_round_trip below.
        // ============================================================
        [Test]
        public void should_not_create_chapter_releases_table()
        {
            var db = WithDapperMigrationTestDb();

            var tableNames = db.Query<string>(
                "SELECT name FROM sqlite_master WHERE type='table'").ToList();

            tableNames.Should().NotContain(
                "ChapterReleases",
                "ChapterReleases table dropped in Phase 16.1 REVERT-02 (entity layer reverted)");
        }

        [Test]
        public void should_not_create_chapter_releases_indexes()
        {
            var db = WithDapperMigrationTestDb();

            var indexNames = db.Query<string>(
                "SELECT name FROM sqlite_master WHERE type='index'").ToList();

            indexNames.Should().NotContain(
                "IX_ChapterReleases_ChapterId_Lang_Group",
                "ChapterReleases natural-key UNIQUE index dropped in Phase 16.1 REVERT-02");

            indexNames.Should().NotContain(
                "IX_ChapterReleases_ChapterId",
                "ChapterReleases per-Chapter lookup index dropped in Phase 16.1 REVERT-02");
        }

        // ============================================================
        // Phase 6 — ChapterHistory table created (HISTORY-01..03; D-21 BL-01 fix)
        // ============================================================
        [Test]
        public void should_create_chapter_history_table_with_independent_chapter_id()
        {
            var db = WithDapperMigrationTestDb();

            var rows = db.Query<TableInfoRow>("PRAGMA table_info(\"ChapterHistory\");").ToList();
            var columnNames = rows.Select(r => r.name).ToList();

            columnNames.Should().Contain("MangaId");
            columnNames.Should().Contain("ChapterId");
            columnNames.Should().Contain("EventType");
            columnNames.Should().Contain("Date");
            columnNames.Should().Contain("SourceTitle");
            columnNames.Should().Contain("DownloadId");
            columnNames.Should().Contain("TranslatedLanguage");
            columnNames.Should().Contain("ScanlationGroup");
            columnNames.Should().Contain("SourceKey");
            columnNames.Should().Contain("ReleaseGuid");
            columnNames.Should().Contain("Data");
            columnNames.Should().Contain("Successful");

            // Round-trip: insert and select.
            db.Execute(
                "INSERT INTO \"ChapterHistory\" (\"MangaId\", \"ChapterId\", \"EventType\", \"Date\", \"Successful\") "
                + "VALUES (@MangaId, @ChapterId, @EventType, @Date, @Successful)",
                new { MangaId = 1, ChapterId = 42, EventType = 1, Date = System.DateTime.UtcNow, Successful = true });

            var roundtripped = db.Query<int>(
                "SELECT \"ChapterId\" FROM \"ChapterHistory\" WHERE \"MangaId\" = 1").Single();

            roundtripped.Should().Be(42, "ChapterHistory.ChapterId is independent of EpisodeHistory.EpisodeId");
        }

        // ============================================================
        // Phase 6 — MangaBlocklist table (BLOCK-01..02; D-11 release identity)
        // ============================================================
        [Test]
        public void should_create_manga_blocklist_table_with_release_identity_columns()
        {
            var db = WithDapperMigrationTestDb();

            var rows = db.Query<TableInfoRow>("PRAGMA table_info(\"MangaBlocklist\");").ToList();
            var columnNames = rows.Select(r => r.name).ToList();

            columnNames.Should().Contain("MangaId");
            columnNames.Should().Contain("ChapterIds");
            columnNames.Should().Contain("SourceTitle");
            columnNames.Should().Contain("SourceKey");
            columnNames.Should().Contain("ReleaseGuid");
            columnNames.Should().Contain("ReleaseInfoJson");
            columnNames.Should().Contain("Date");
            columnNames.Should().Contain("Reason");
            columnNames.Should().Contain("Source");

            // Round-trip the (SourceKey, ReleaseGuid, Title) identity triple.
            db.Execute(
                "INSERT INTO \"MangaBlocklist\" (\"MangaId\", \"SourceTitle\", \"SourceKey\", \"ReleaseGuid\", \"Date\") "
                + "VALUES (@MangaId, @SourceTitle, @SourceKey, @ReleaseGuid, @Date)",
                new { MangaId = 1, SourceTitle = "Test [grp].cbz", SourceKey = "mangadex", ReleaseGuid = "abc-123", Date = System.DateTime.UtcNow });

            var got = db.Query<(string SourceKey, string ReleaseGuid, string Title)>(
                "SELECT \"SourceKey\", \"ReleaseGuid\", \"SourceTitle\" AS Title FROM \"MangaBlocklist\" WHERE \"MangaId\" = 1").Single();

            got.SourceKey.Should().Be("mangadex");
            got.ReleaseGuid.Should().Be("abc-123");
            got.Title.Should().Be("Test [grp].cbz");
        }

        // ============================================================
        // Phase 6 — ChapterFiles table (PIPELINE-04 import artifact).
        // Phase 16.1 REVERT-06 + D-06: 2 group-axis columns (TranslatedLanguage +
        // ScanlationGroup), not 3 — ReleaseGroup absorbed into ScanlationGroup.
        // ============================================================
        [Test]
        public void should_create_chapter_files_table_round_trip()
        {
            var db = WithDapperMigrationTestDb();

            var rows = db.Query<TableInfoRow>("PRAGMA table_info(\"ChapterFiles\");").ToList();
            var columnNames = rows.Select(r => r.name).ToList();

            columnNames.Should().Contain("MangaId");
            columnNames.Should().Contain("ChapterId");
            columnNames.Should().Contain("RelativePath");
            columnNames.Should().Contain("Path");
            columnNames.Should().Contain("Size");
            columnNames.Should().Contain("DateAdded");

            // Phase 16.1 D-06: TranslatedLanguage + ScanlationGroup are the two canonical
            // group-axis columns; ReleaseGroup column is dropped.
            columnNames.Should().Contain(
                "TranslatedLanguage",
                "TranslatedLanguage column survives — Phase 6 PIPELINE-04 axis preserved");
            columnNames.Should().Contain(
                "ScanlationGroup",
                "ScanlationGroup is the canonical \"release group\" axis for manga (Phase 16.1 D-06)");
            columnNames.Should().NotContain(
                "ReleaseGroup",
                "ReleaseGroup column absorbed into ScanlationGroup (Phase 16.1 REVERT-06 + D-06)");

            db.Execute(
                "INSERT INTO \"ChapterFiles\" (\"MangaId\", \"ChapterId\", \"RelativePath\", \"Path\", \"Size\", \"DateAdded\") "
                + "VALUES (@MangaId, @ChapterId, @RelativePath, @Path, @Size, @DateAdded)",
                new { MangaId = 1, ChapterId = 7, RelativePath = "ch7.cbz", Path = "/lib/ch7.cbz", Size = 1234L, DateAdded = System.DateTime.UtcNow });

            var got = db.Query<long>("SELECT \"Size\" FROM \"ChapterFiles\" WHERE \"MangaId\" = 1").Single();
            got.Should().Be(1234L);
        }

        // ============================================================
        // Phase 6 D-10 — UpgradeAllowed columns + Manga.UpgradeAllowedOverride
        // ============================================================
        [Test]
        public void should_have_upgrade_allowed_columns_with_correct_defaults()
        {
            var db = WithDapperMigrationTestDb();

            var translationProfileCols = db.Query<TableInfoRow>("PRAGMA table_info(\"TranslationProfiles\");").ToList();
            var customFormatProfileCols = db.Query<TableInfoRow>("PRAGMA table_info(\"CustomFormatProfiles\");").ToList();
            var mangaCols = db.Query<TableInfoRow>("PRAGMA table_info(\"Manga\");").ToList();
            var chapterCols = db.Query<TableInfoRow>("PRAGMA table_info(\"Chapters\");").ToList();

            var tpUpgradeAllowed = translationProfileCols.Single(c => c.name == "UpgradeAllowed");
            var cfpUpgradeAllowed = customFormatProfileCols.Single(c => c.name == "UpgradeAllowed");
            var mangaOverride = mangaCols.Single(c => c.name == "UpgradeAllowedOverride");
            var chapterFileId = chapterCols.Single(c => c.name == "ChapterFileId");

            // TranslationProfile.UpgradeAllowed default true (D-10 — language rank ordered).
            tpUpgradeAllowed.notnull.Should().Be(1, "TranslationProfile.UpgradeAllowed is non-nullable");
            tpUpgradeAllowed.dflt_value.Should().Be("1", "TranslationProfile.UpgradeAllowed default true (D-10)");

            // CustomFormatProfile.UpgradeAllowed default false (D-10 — manga CF scores subjective).
            cfpUpgradeAllowed.notnull.Should().Be(1, "CustomFormatProfile.UpgradeAllowed is non-nullable");
            cfpUpgradeAllowed.dflt_value.Should().Be("0", "CustomFormatProfile.UpgradeAllowed default false (D-10)");

            // Manga.UpgradeAllowedOverride is nullable; null = fall back to per-Profile flag.
            mangaOverride.notnull.Should().Be(0, "Manga.UpgradeAllowedOverride must be nullable for three-state semantics");

            // Chapter.ChapterFileId is nullable; null = no ChapterFile imported yet.
            chapterFileId.notnull.Should().Be(0, "Chapter.ChapterFileId must be nullable (null = no file imported)");
        }

        // ============================================================
        // Phase 6 D-07 — IndexerDefinition SyncInterval + LastRssSync columns
        // ============================================================
        [Test]
        public void should_add_indexer_sync_interval_columns()
        {
            var db = WithDapperMigrationTestDb();

            var cols = db.Query<TableInfoRow>("PRAGMA table_info(\"Indexers\");").ToList();

            var syncInterval = cols.Single(c => c.name == "SyncInterval");
            var lastRssSync = cols.Single(c => c.name == "LastRssSync");

            // SyncInterval default 0 = use global Config.MangaRssSyncInterval.
            syncInterval.notnull.Should().Be(1, "Indexer.SyncInterval is non-nullable");
            syncInterval.dflt_value.Should().Be("0", "Indexer.SyncInterval default 0 = use global Config.MangaRssSyncInterval");

            // LastRssSync nullable; null = never synced.
            lastRssSync.notnull.Should().Be(0, "Indexer.LastRssSync must be nullable (null = never synced)");
        }

        // ============================================================
        // Phase 6 — required indexes exist (ChapterHistory + MangaBlocklist + ChapterFile)
        // ============================================================
        [Test]
        public void should_create_phase_six_indexes()
        {
            var db = WithDapperMigrationTestDb();

            var indexes = db.Query<IndexInfo>(
                "SELECT name, tbl_name AS TableName, sql FROM sqlite_master WHERE type='index' AND sql IS NOT NULL")
                .ToList();

            indexes.Should().Contain(i => i.Name == "IX_ChapterHistory_ChapterId", "IX_ChapterHistory_ChapterId must exist");
            indexes.Should().Contain(i => i.Name == "IX_ChapterHistory_MangaId_Date", "IX_ChapterHistory_MangaId_Date must exist");
            indexes.Should().Contain(i => i.Name == "IX_ChapterHistory_DownloadId", "IX_ChapterHistory_DownloadId must exist");
            indexes.Should().Contain(i => i.Name == "IX_MangaBlocklist_MangaId", "IX_MangaBlocklist_MangaId must exist");
            indexes.Should().Contain(i => i.Name == "IX_MangaBlocklist_ReleaseGuid", "IX_MangaBlocklist_ReleaseGuid must exist");
            indexes.Should().Contain(i => i.Name == "IX_ChapterFile_MangaId", "IX_ChapterFile_MangaId must exist");
            indexes.Should().Contain(i => i.Name == "IX_ChapterFile_ChapterId", "IX_ChapterFile_ChapterId must exist");
            indexes.Should().Contain(i => i.Name == "IX_Chapter_ChapterFileId", "IX_Chapter_ChapterFileId must exist");
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
