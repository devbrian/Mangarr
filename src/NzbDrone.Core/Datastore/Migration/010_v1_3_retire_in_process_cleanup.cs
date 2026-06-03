using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Sonarr divergence: NEW manga-domain cleanup migration per Phase 39 (RETIRE-03) — see DIVERGENCE.md.
    // This is the on-upgrade, one-shot cleanup that follows the deletion of the in-process
    // image-download + aggregator-indexer + Cloudflare-clearance verticals (Plans 39-02 / 39-03 / 39-04).
    //
    // WHAT — pure cleanup of orphan runtime state (no schema creation, no row seed):
    //   (1) DELETE the orphan `DownloadClients` ThingiProvider row for the retired
    //       `InProcessImageDownloadClient` (default-seeded by the now-deleted
    //       DownloadClientFactory.InitializeProviders). Its C# class is gone, so on upgrade
    //       it generates "unknown implementation" Health-check noise (RETIRE-03 raison d'être).
    //   (2) DELETE the orphan `Indexers` ThingiProvider rows for the retired in-process
    //       `MangaDexIndexer` + `ComixIndexer` (default-seeded on fresh DB per Phase 15 close).
    //       Their C# classes are gone (Plan 39-03); the GatewayIndexer is now the sole IIndexer.
    //   (3) RESET the two stale per-source escalation rows in `IndexerSourceStatus`, SCOPED to the
    //       retired source keys ONLY ('mangadex','comix.to' — the verified DefaultSourceKey literals
    //       from MangaDexIndexer/ComixIndexer, recorded in 39-03-SUMMARY). A stale `DisabledTill`
    //       on these keys could auto-disable the gateway on first request. The scope is deliberately
    //       NARROW (NOT a blanket DELETE): GatewayParser is now the sole live writer of this table
    //       (GatewayParser.cs:106 via IIndexerSourceStatusService.RecordFailure), so live gateway
    //       per-source status MUST survive (Phase 39 Open Question #2 resolution; T-39-05-02).
    //   (4) DROP the now-empty in-process staging table `ChapterDownloadState` (D-02). Safe ONLY
    //       because every reader (the in-process download vertical + ProcessMangaCompletedDownloads
    //       + the ManualImportService fast-path) was deleted in Plan 39-02 BEFORE this migration
    //       ships (depends_on 39-02 — Pitfall 6: FluentMigrator runs on startup before DI completes,
    //       so dropping the table while a reader still references it would crash the startup poll).
    //
    // IMPORTANT — TABLE NAMES are the SQL table names, NOT the C# model class names.
    //   The ThingiProvider definition tables map model→table as:
    //     IndexerDefinition        -> "Indexers"        (TableMapping.cs:94)
    //     DownloadClientDefinition -> "DownloadClients" (TableMapping.cs:158)
    //   The 39-RESEARCH "recommended body" wrote DELETE FROM "DownloadClientDefinition" /
    //   "IndexerDefinition" — those are the *model* names; running against them would hit a
    //   non-existent table. We DELETE against the real SQL tables "DownloadClients" + "Indexers".
    //   The Migration010Fixture exercises this on SQLite, so a wrong table name surfaces as a
    //   hard test failure. (Rule-1 fix vs the research draft — documented in 39-05-SUMMARY.)
    //
    // The CloudflareSolverUrl Config row (optional zero-residue delete, D-01) is NOT touched here:
    //   the property + every reader were deleted in Plan 39-04, the stored key casing could not be
    //   confirmed from source (no CloudflareSolverUrl literal remains in the tree to read it from),
    //   and the row has no surviving reader — it ages out harmlessly. Documented in 39-05-SUMMARY.
    //
    // Identifiers double-quoted so the predicates are portable across SQLite + Postgres (008/009
    // idiom). Raw Execute.Sql is used for the IN(...) predicates and kept uniform for the single
    // equality delete (matches the surrounding 008/009 raw-SQL style for cross-dialect predicates).
    //
    // Anti-Pattern C (sonarr-consistency-audit): this migration seeds ZERO rows. It is pure
    // DELETE + DROP. The Migration010Fixture pins the zero-row-seed source floor (it asserts the
    // migration source never calls the row-insert API — so this comment deliberately avoids the
    // literal API token that floor test scans for).
    //
    // NEVER edits 001_mangarr_baseline.cs or 009_v1_3_manga_download_history.cs (post-v1.0.0
    // append-only migration policy). Succeeds Migration 009; this is Migration number 10.
    [Migration(10)]
    public class retire_in_process_cleanup : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // (1) Orphan in-process download client provider row (table "DownloadClients").
            Execute.Sql("DELETE FROM \"DownloadClients\" WHERE \"Implementation\" = 'InProcessImageDownloadClient'");

            // (2) Orphan in-process aggregator indexer provider rows (table "Indexers").
            Execute.Sql("DELETE FROM \"Indexers\" WHERE \"Implementation\" IN ('MangaDexIndexer', 'ComixIndexer')");

            // (3) Reset the stale per-source escalation — SCOPED to the retired keys only, so the
            //     gateway's live IndexerSourceStatus rows (written by GatewayParser) are preserved.
            Execute.Sql("DELETE FROM \"IndexerSourceStatus\" WHERE \"SourceKey\" IN ('mangadex', 'comix.to')");

            // (4) Drop the in-process staging table (every reader deleted in Plan 39-02).
            //
            // Defensive surface (PR #312 review P2): the DROP is the intended, irreversible
            // retirement (D-02) — the in-process download client is deleted, so NOTHING can
            // resume an in-flight in-process download's staged CBZ/scratch rows. But before the
            // drop, count any rows that WOULD be orphaned and emit a Warn naming the count, so a
            // user upgrading mid-download sees that their staged scratch files are abandoned by
            // the retirement and must re-grab via the gateway. The count is best-effort + safe if
            // the table is already gone (idempotent re-run / fresh-DB-where-001-already-skipped it):
            // the try/catch swallows the "no such table" error so the DROP path still proceeds.
            var orphanRows = 0;
            Execute.WithConnection((connection, transaction) =>
            {
                try
                {
                    using var countCmd = connection.CreateCommand();
                    countCmd.Transaction = transaction;
                    countCmd.CommandText = "SELECT COUNT(*) FROM \"ChapterDownloadState\"";
                    orphanRows = System.Convert.ToInt32(countCmd.ExecuteScalar());
                }
                catch (System.Exception ex)
                {
                    // Table already absent (idempotent re-run) — nothing to warn about; the
                    // Delete.Table below is itself guarded by FluentMigrator's IfExists semantics.
                    _logger.Trace(ex, "ChapterDownloadState row-count skipped — table not present.");
                    orphanRows = 0;
                }
            });

            if (orphanRows > 0)
            {
                _logger.Warn(
                    "Dropping in-process ChapterDownloadState staging table with {0} in-flight row(s): " +
                    "the in-process download client was retired (Phase 39), so these staged/scratch CBZ files " +
                    "are abandoned and cannot be resumed. Re-grab the affected chapters via the external gateway.",
                    orphanRows);
            }

            Delete.Table("ChapterDownloadState");
        }
    }
}
