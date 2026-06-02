using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Sonarr divergence: NEW manga sibling per Phase 36 (LOOP-02) — see DIVERGENCE.md.
    // Provenance: v5-develop:src/NzbDrone.Core/Download/History/DownloadHistory.cs
    //   (the upstream DownloadId-keyed matching join this RESHAPE re-skins to the manga domain).
    // Role-match analog (table-create + index shape): 001_mangarr_baseline.cs:352-374
    //   (Create.TableForModel + Create.Index().OnTable().OnColumn().Ascending()).
    // FluentMigrator file-shape analog: 008_v1_3_postgres_utc_datetime_column_sweep.cs
    //   ([Migration(N)], NzbDroneMigrationBase, MainDbUpgrade(), ProcessorIdConstants.PostgreSQL guard).
    //
    // WHAT — a RESHAPE, not a pure add:
    //   (1) DROP the orphan TV-shape `DownloadHistory` table created at 001_mangarr_baseline.cs:448.
    //       It is SeriesId-shaped (`SeriesId` NOT NullABLE), has ZERO rows on any live DB, and ZERO
    //       consumers in the tree (the TV DownloadHistory{Service,Repository} were deleted in the
    //       Phase 15 Tv/ cutover — only the baseline table-create survived as dead schema). Dropping
    //       it is safe: nothing reads it, so there is no crash window even though FluentMigrator runs
    //       on startup before DI completes (T-36-01-02 accept).
    //   (2) CREATE the lean `MangaDownloadHistory` matching join — the DISTINCT second surface that
    //       powers the LOOP-02 DownloadId-keyed matcher. This is NOT the user-facing ChapterHistory
    //       (History/Manga/ChapterHistory.cs). Sonarr deliberately keeps two surfaces: a rich,
    //       retention-swept user history (ChapterHistory) AND a lean, never-swept matching join
    //       (this table) keyed by DownloadId so a poll landing right after a grab can resolve the
    //       manga + chapters without a title-parse fallback. Do NOT reuse ChapterHistory.FindByDownloadId.
    //
    // Date column under Postgres: created as `timestamptz` up-front (under an IfDatabase(PostgreSQL)
    // ALTER, mirroring the 007/008 idiom) so the repo-wide UTC-DateTime sweep never has to revisit it.
    // The DapperUtcConverter (TableMapping.cs) round-trips DateTimeKind.Utc; on SQLite the .AsDateTime()
    // column stores text and round-trips identically, so the ALTER is Postgres-only and skipped on SQLite.
    //
    // Anti-Pattern C (sonarr-consistency-audit): this migration seeds ZERO rows. Scheduled-task /
    // poll-cadence rows register at runtime via Jobs/TaskManager.defaultTasks, NEVER a migration seed.
    // The Migration009Fixture pins this zero-row-seed source floor.
    //
    // NEVER edits 001_mangarr_baseline.cs (post-v1.0.0 append-only migration policy). Succeeds
    // Migration 008. Does NOT touch ChapterDownloadState (a Phase-39 concern).
    [Migration(9)]
    public class manga_download_history : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // (1) Drop the orphan TV-shape table — zero rows, zero consumers (001:448).
            Delete.Table("DownloadHistory");

            // (2) Create the lean DownloadId → manga/chapters matching join.
            Create.TableForModel("MangaDownloadHistory")
                .WithColumn("DownloadId").AsString().NotNullable()
                .WithColumn("MangaId").AsInt32().NotNullable()
                .WithColumn("ChapterIds").AsString().NotNullable()      // JSON List<int> (multi-chapter packs, Pitfall 2)
                .WithColumn("EventType").AsInt32().NotNullable()
                .WithColumn("SourceTitle").AsString().Nullable()
                .WithColumn("Date").AsDateTime().NotNullable()
                .WithColumn("Data").AsString().Nullable();              // JSON Dictionary<string,string>

            Create.Index().OnTable("MangaDownloadHistory").OnColumn("DownloadId").Ascending();
            Create.Index().OnTable("MangaDownloadHistory").OnColumn("MangaId").Ascending();

            // Postgres: a UTC instant cannot cleanly round-trip through `timestamp without time
            // zone` (007/008 precedent). Create it `timestamptz` so the repo-wide sweep (Issue #270)
            // never has to revisit this column. Identifiers double-quoted so Postgres preserves case.
            // Skipped under the SQLite processor (MigrationController maps DatabaseType → ProcessorId).
            IfDatabase(ProcessorIdConstants.PostgreSQL)
                .Execute.Sql("ALTER TABLE \"MangaDownloadHistory\" ALTER COLUMN \"Date\" TYPE timestamptz USING \"Date\" AT TIME ZONE 'UTC'");
        }
    }
}
