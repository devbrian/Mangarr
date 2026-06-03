using System.IO;
using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    // Phase 39 Plan 39-05 (RETIRE-03) — pins Migration 010 behavior: the one-shot cleanup of
    // orphan runtime state left after the in-process download + aggregator-indexer + Cloudflare
    // verticals were deleted (Plans 39-02 / 39-03 / 39-04). It:
    //   (1) DELETEs the orphan "DownloadClients" row WHERE Implementation='InProcessImageDownloadClient',
    //   (2) DELETEs the orphan "Indexers" rows WHERE Implementation IN ('MangaDexIndexer','ComixIndexer'),
    //   (3) RESETs "IndexerSourceStatus" rows scoped to SourceKey IN ('mangadex','comix.to') ONLY
    //       (a live gateway-key row MUST survive — proves the reset is scoped, not blanket), and
    //   (4) DROPs the now-empty "ChapterDownloadState" staging table.
    //
    // NOTE on table names: the ThingiProvider definition tables are "Indexers" + "DownloadClients"
    // (the *SQL* table names — IndexerDefinition/DownloadClientDefinition are the C# model class
    // names, mapped via TableMapping.cs). This fixture seeds + asserts against the real SQL tables,
    // so a wrong table name in Migration 010 surfaces here as a hard failure.
    //
    // Because the migration DELETEs/DROPs, each row-delete test SEEDS the row in the
    // pre-migration schema via WithDapperMigrationTestDb(beforeMigration: ...) (the canonical
    // seed-then-assert idiom established by Migration005Fixture / Migration006Fixture), then
    // asserts it is gone after the migration applies. The ChapterDownloadState drop is asserted
    // via sqlite_master introspection (mirrors Migration009Fixture's table-existence checks).
    //
    // The final test enforces the Anti-Pattern C zero-Insert.IntoTable source floor by reading
    // the migration file text directly (using the robust walk-up + Assert.Inconclusive pattern
    // copied from Migration009Fixture — NOT a hardcoded Parent hop, per the CI-relocation lesson).
    [TestFixture]
    [Category("SqliteOnly")]
    public class Migration010Fixture : MigrationTest<retire_in_process_cleanup>
    {
        // ============================================================
        // Test 1 — the orphan in-process DownloadClients row is deleted.
        // ============================================================
        [Test]
        public void should_delete_orphan_inprocess_download_client_row()
        {
            var db = WithDapperMigrationTestDb(beforeMigration: m =>
            {
                m.Execute.Sql(
                    "INSERT INTO \"DownloadClients\" (\"Enable\", \"Name\", \"Implementation\", \"ConfigContract\", \"Priority\", \"RemoveCompletedDownloads\", \"RemoveFailedDownloads\") " +
                    "VALUES (1, 'InProcess', 'InProcessImageDownloadClient', 'InProcessImageDownloadClientSettings', 1, 1, 1)");
            });

            var count = db.Query<int>(
                "SELECT COUNT(*) FROM \"DownloadClients\" WHERE \"Implementation\" = 'InProcessImageDownloadClient'").Single();
            count.Should().Be(0, "Migration 010 must delete the orphan InProcessImageDownloadClient provider row (retired class — 'unknown implementation' health noise)");
        }

        // ============================================================
        // Test 2 — the orphan in-process MangaDex + Comix Indexers rows are deleted.
        // ============================================================
        [Test]
        public void should_delete_orphan_mangadex_and_comix_indexer_rows()
        {
            var db = WithDapperMigrationTestDb(beforeMigration: m =>
            {
                m.Execute.Sql(
                    "INSERT INTO \"Indexers\" (\"Name\", \"Implementation\", \"ConfigContract\", \"Priority\", \"DownloadClientId\") " +
                    "VALUES ('MangaDex', 'MangaDexIndexer', 'MangaDexIndexerSettings', 25, 0)");
                m.Execute.Sql(
                    "INSERT INTO \"Indexers\" (\"Name\", \"Implementation\", \"ConfigContract\", \"Priority\", \"DownloadClientId\") " +
                    "VALUES ('Comix', 'ComixIndexer', 'ComixIndexerSettings', 25, 0)");
            });

            var count = db.Query<int>(
                "SELECT COUNT(*) FROM \"Indexers\" WHERE \"Implementation\" IN ('MangaDexIndexer', 'ComixIndexer')").Single();
            count.Should().Be(0, "Migration 010 must delete both orphan in-process aggregator indexer rows (GatewayIndexer is now the sole IIndexer)");
        }

        // ============================================================
        // Test 3 — IndexerSourceStatus reset is SCOPED: the two retired-key rows are
        //          deleted, but a live gateway-key row SURVIVES (proves the predicate is
        //          scoped to the retired keys, NOT a blanket DELETE — T-39-05-02).
        // ============================================================
        [Test]
        public void should_reset_stale_indexer_source_status_for_retired_keys_only()
        {
            var db = WithDapperMigrationTestDb(beforeMigration: m =>
            {
                // Two stale retired-key rows with a non-null DisabledTill + non-zero EscalationLevel
                // (the auto-disable hazard Migration 010 clears).
                m.Execute.Sql(
                    "INSERT INTO \"IndexerSourceStatus\" (\"SourceKey\", \"EscalationLevel\", \"DisabledTill\") " +
                    "VALUES ('mangadex', 3, '2099-01-01 00:00:00')");
                m.Execute.Sql(
                    "INSERT INTO \"IndexerSourceStatus\" (\"SourceKey\", \"EscalationLevel\", \"DisabledTill\") " +
                    "VALUES ('comix.to', 5, '2099-01-01 00:00:00')");

                // A live gateway-key row written by GatewayParser — MUST survive the scoped reset.
                m.Execute.Sql(
                    "INSERT INTO \"IndexerSourceStatus\" (\"SourceKey\", \"EscalationLevel\", \"DisabledTill\") " +
                    "VALUES ('gateway-live-source', 2, '2099-01-01 00:00:00')");
            });

            var retiredCount = db.Query<int>(
                "SELECT COUNT(*) FROM \"IndexerSourceStatus\" WHERE \"SourceKey\" IN ('mangadex', 'comix.to')").Single();
            retiredCount.Should().Be(0, "Migration 010 must reset the two stale retired-key escalation rows so a stale DisabledTill cannot auto-disable the gateway");

            var liveSurvives = db.Query<int>(
                "SELECT COUNT(*) FROM \"IndexerSourceStatus\" WHERE \"SourceKey\" = 'gateway-live-source'").Single();
            liveSurvives.Should().Be(1, "the reset MUST be scoped to the retired keys — live gateway per-source status (written by GatewayParser) MUST survive (T-39-05-02)");
        }

        // ============================================================
        // Test 4 — the now-empty ChapterDownloadState staging table is dropped.
        // ============================================================
        [Test]
        public void should_drop_chapter_download_state_table()
        {
            var db = WithDapperMigrationTestDb();

            var count = db.Query<int>(
                "SELECT COUNT(*) FROM \"sqlite_master\" WHERE \"type\" = 'table' AND \"name\" = 'ChapterDownloadState'").Single();
            count.Should().Be(0, "Migration 010 must drop the now-empty in-process ChapterDownloadState staging table (D-02; safe — every reader deleted in Plan 39-02)");
        }

        // ============================================================
        // Test 5 — the ChapterDownloadState drop SUCCEEDS even when the table is seeded
        //          NON-EMPTY (an in-flight in-process download mid-upgrade). The drop is the
        //          intended irreversible retirement (D-02); the migration emits a defensive
        //          Warn naming the orphaned-row count BEFORE the drop (PR #312 review P2). This
        //          exercises the warn path (orphanRows > 0 branch) and proves the drop still
        //          succeeds with rows present — the table is gone afterward regardless.
        // ============================================================
        [Test]
        public void should_drop_chapter_download_state_table_even_when_seeded_non_empty()
        {
            var db = WithDapperMigrationTestDb(beforeMigration: m =>
            {
                // One in-flight in-process download row — the orphan the Warn path names.
                m.Execute.Sql(
                    "INSERT INTO \"ChapterDownloadState\" " +
                    "(\"MangaId\", \"ChapterId\", \"Title\", \"RemoteChapterJson\", \"TotalPages\", \"CompletedPages\", " +
                    " \"EstimatedSizeBytes\", \"ScratchDir\", \"Status\", \"CreatedAt\", \"UpdatedAt\") " +
                    "VALUES " +
                    "(1, 1, 'TestKit in-flight chapter', '{}', 10, 3, 0, '/scratch/1', 1, '2026-01-01 00:00:00', '2026-01-01 00:00:00')");
            });

            // The migration ran with a non-empty table (the Warn path fired); the table is dropped.
            var count = db.Query<int>(
                "SELECT COUNT(*) FROM \"sqlite_master\" WHERE \"type\" = 'table' AND \"name\" = 'ChapterDownloadState'").Single();
            count.Should().Be(0, "Migration 010 must drop ChapterDownloadState even when it holds in-flight rows (D-02 irreversible retirement; the orphan-row Warn is informational, not a halt)");
        }

        // ============================================================
        // Test 6 — Anti-Pattern C: the migration source contains ZERO Insert.IntoTable
        //          calls. This is a pure DELETE + DROP cleanup; it seeds no rows.
        // ============================================================
        [Test]
        public void Migration_010_contains_zero_Insert_IntoTable_calls()
        {
            var migrationSource = ReadMigrationSource();

            // Strip comments before counting — a `// ... Insert.IntoTable ...` documentation
            // note (Migration 010's own anti-pattern-C explainer mentions the token by name)
            // must not trip the gate. Count only non-commented occurrences, mirroring the
            // textual guard in TaskManagerDefaultTasksFixture.Migration_001_contains_zero_Insert_IntoTable_calls.
            var hits = 0;
            var inBlockComment = false;
            foreach (var raw in migrationSource.Split('\n'))
            {
                var trimmed = raw.TrimStart();

                // Crude /* ... */ block-comment tracking: skip lines fully inside a block comment.
                if (inBlockComment)
                {
                    if (trimmed.Contains("*/"))
                    {
                        inBlockComment = false;
                    }

                    continue;
                }

                if (trimmed.StartsWith("/*") && !trimmed.Contains("*/"))
                {
                    inBlockComment = true;
                    continue;
                }

                // Skip whole-line `//` comments.
                if (trimmed.StartsWith("//"))
                {
                    continue;
                }

                // Strip a trailing `// ...` line-comment so an inline note after real code
                // can't smuggle the token past the gate either.
                var commentIdx = trimmed.IndexOf("//", System.StringComparison.Ordinal);
                var codePart = commentIdx >= 0 ? trimmed.Substring(0, commentIdx) : trimmed;

                if (codePart.Contains("Insert.IntoTable"))
                {
                    hits++;
                }
            }

            hits.Should().Be(0,
                "Migration 010 must NOT seed any rows (Anti-Pattern C — it is a pure orphan-state cleanup; comment mentions of the token are stripped before counting)");
        }

        private static string ReadMigrationSource()
        {
            // Resolve the migration .cs from the source tree. A hardcoded Parent-hop from
            // TestContext.TestDirectory is brittle: it is only correct for the local
            // <repo>/_tests/net10.0 layout and resolves wrong on the CI unit_test job, which runs
            // from a stripped, relocated test artifact (GH Actions: /home/runner/work/<repo>/<repo>/…).
            // Walk upward from TestDirectory looking for the src/NzbDrone.Core marker; if the source
            // tree is not reachable, this textual guard cannot run here — mark inconclusive rather
            // than failing the build (mirrors Migration009Fixture / TaskManagerDefaultTasksFixture).
            var srcRoot = FindCoreSourceRoot(TestContext.CurrentContext.TestDirectory);
            if (srcRoot == null)
            {
                Assert.Inconclusive(
                    "src/NzbDrone.Core is not reachable from the test directory — the Migration 010 "
                    + "Insert.IntoTable textual guard runs in dev builds and checkout-based CI jobs, "
                    + "not from a standalone test artifact.");
            }

            var migrationPath = Path.Combine(srcRoot!, "Datastore", "Migration", "010_v1_3_retire_in_process_cleanup.cs");

            File.Exists(migrationPath).Should().BeTrue(
                $"the Migration 010 source file must exist at {migrationPath}");

            return File.ReadAllText(migrationPath);
        }

        // Walk up the directory chain from startDir, returning the first ancestor's
        // src/NzbDrone.Core that actually contains the Migration 010 file — or null if the source
        // tree is not present in this layout (e.g. the stripped unit_test CI artifact).
        private static string FindCoreSourceRoot(string startDir)
        {
            for (var dir = new DirectoryInfo(startDir); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "src", "NzbDrone.Core");
                if (File.Exists(Path.Combine(candidate, "Datastore", "Migration", "010_v1_3_retire_in_process_cleanup.cs")))
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
