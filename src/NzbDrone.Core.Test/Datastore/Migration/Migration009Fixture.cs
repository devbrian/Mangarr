using System.IO;
using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    // Phase 36 Plan 36-01 (LOOP-02) — pins Migration 009 behavior: a RESHAPE that
    //   (1) DROPs the orphan TV-shape `DownloadHistory` table (001_mangarr_baseline.cs:448 —
    //       SeriesId-shaped, zero rows, zero consumers), and
    //   (2) CREATEs the lean `MangaDownloadHistory` matching join with the 7 columns +
    //       DownloadId + MangaId indexes.
    //
    // Mirrors the SqliteOnly PRAGMA + sqlite_master introspection shape established by
    // Migration005Fixture / Migration006Fixture and the sibling baseline fixtures.
    //
    // The final test enforces the Anti-Pattern C zero-Insert.IntoTable source floor by
    // reading the migration file text directly (scheduled-task / poll-cadence rows belong
    // in Jobs/TaskManager.defaultTasks, NEVER a migration seed).
    [TestFixture]
    [Category("SqliteOnly")]
    public class Migration009Fixture : MigrationTest<manga_download_history>
    {
        // ============================================================
        // Test 1 — the orphan TV-shape DownloadHistory table is dropped.
        // ============================================================
        [Test]
        public void should_drop_orphan_download_history_table()
        {
            var db = WithDapperMigrationTestDb();

            var count = db.Query<int>(
                "SELECT COUNT(*) FROM \"sqlite_master\" WHERE \"type\" = 'table' AND \"name\" = 'DownloadHistory'").Single();
            count.Should().Be(0, "Migration 009 must drop the orphan TV-shape DownloadHistory table (001:448 — zero rows, zero consumers)");
        }

        // ============================================================
        // Test 2 — the MangaDownloadHistory table exists post-migration.
        // ============================================================
        [Test]
        public void should_create_manga_download_history_table()
        {
            var db = WithDapperMigrationTestDb();

            var count = db.Query<int>(
                "SELECT COUNT(*) FROM \"sqlite_master\" WHERE \"type\" = 'table' AND \"name\" = 'MangaDownloadHistory'").Single();
            count.Should().Be(1, "Migration 009 must create the lean MangaDownloadHistory matching join");
        }

        // ============================================================
        // Test 3 — MangaDownloadHistory carries all 7 declared columns (+ Id from ModelBase).
        // ============================================================
        [Test]
        public void should_create_manga_download_history_with_all_columns()
        {
            var db = WithDapperMigrationTestDb();

            var columns = db.Query<string>(
                "SELECT \"name\" FROM pragma_table_info('MangaDownloadHistory')").ToList();

            columns.Should().Contain("Id", "ModelBase autoincrement PK");
            columns.Should().Contain("DownloadId");
            columns.Should().Contain("MangaId");
            columns.Should().Contain("ChapterIds");
            columns.Should().Contain("EventType");
            columns.Should().Contain("SourceTitle");
            columns.Should().Contain("Date");
            columns.Should().Contain("Data");
        }

        // ============================================================
        // Test 4 — both matching-path indexes (DownloadId + MangaId) exist.
        // ============================================================
        [Test]
        public void should_create_downloadid_and_mangaid_indexes()
        {
            var db = WithDapperMigrationTestDb();

            var indexedColumns = db.Query<string>(
                "SELECT \"ii\".\"name\" FROM \"sqlite_master\" AS \"m\" " +
                "JOIN pragma_index_info(\"m\".\"name\") AS \"ii\" " +
                "WHERE \"m\".\"type\" = 'index' AND \"m\".\"tbl_name\" = 'MangaDownloadHistory'").ToList();

            indexedColumns.Should().Contain("DownloadId",
                "the DownloadId index powers the GetLatestGrab matcher lookup (LOOP-02 hot path)");
            indexedColumns.Should().Contain("MangaId",
                "the MangaId index powers per-manga history queries");
        }

        // ============================================================
        // Test 5 — Anti-Pattern C: the migration source contains ZERO Insert.IntoTable calls.
        //          Scheduled-task / cadence rows register in TaskManager.defaultTasks at
        //          runtime, NEVER a migration seed (sonarr-consistency-audit gate).
        // ============================================================
        [Test]
        public void Migration_009_contains_zero_Insert_IntoTable_calls()
        {
            var migrationSource = ReadMigrationSource();

            migrationSource.Should().NotContain("Insert.IntoTable",
                "Migration 009 must NOT seed any rows (Anti-Pattern C — scheduled tasks live in TaskManager.defaultTasks)");
        }

        private static string ReadMigrationSource()
        {
            // Resolve the migration .cs from the test working directory back to the source tree.
            // TestContext.TestDirectory is _tests/net10.0; the repo root is 2 levels up.
            var testDir = TestContext.CurrentContext.TestDirectory;
            var repoRoot = new DirectoryInfo(testDir).Parent?.Parent?.FullName;
            repoRoot.Should().NotBeNull("the repo root must be resolvable from the test directory");

            var migrationPath = Path.Combine(repoRoot!, "src", "NzbDrone.Core", "Datastore", "Migration", "009_v1_3_manga_download_history.cs");

            File.Exists(migrationPath).Should().BeTrue(
                $"the Migration 009 source file must exist at {migrationPath}");

            return File.ReadAllText(migrationPath);
        }
    }
}
