using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Phase 34 v1.2 INSERTED 2026-05-23 — PARSE2-01 (tracker v1.1-09 carry-forward
    // from Phase 16.1 STRUCT-04):
    //   D-01: storage-layer fix (NOT a BeCloseTo test-side helper) — alter the
    //         column type so the value round-trips bit-for-bit.
    //   D-02: sequential post-baseline migration; NEVER edits 001_mangarr_baseline.cs.
    //   D-03: timestamptz + `USING ... AT TIME ZONE 'UTC'` idiom.
    //
    // WHY: `FirstReleaseDate` is declared `.AsDateTime().Nullable()` at
    // 001_mangarr_baseline.cs:533 → Postgres `timestamp without time zone`. Npgsql
    // 10.0.2 cannot cleanly round-trip a `DateTimeKind.Utc` value through a TZ-less
    // column (offset-shift on write/read). Switching to `timestamptz` (the type
    // Npgsql expects for a UTC instant) eliminates the shift and makes the failing
    // ChapterRepositoryFixture.Insert_persists_FirstReleaseDate_round_trip pass on
    // the Postgres-secondary backend.
    //
    // READ path is ALREADY correct — do NOT add a Dapper type handler / SpecifyKind
    // call site / Npgsql flag. `DapperUtcConverter` (UtcConverter.cs) re-asserts
    // `DateTime.SpecifyKind(..., DateTimeKind.Utc)` on read and `ToUniversalTime()`
    // on write; it is registered globally (TableMapping.cs:295-296) for both
    // `DateTime` and `DateTime?`. The migration alone is sufficient.
    //
    // Postgres-only via IfDatabase(ProcessorIdConstants.PostgreSQL): the
    // MigrationController maps DatabaseType.PostgreSQL → ProcessorIdConstants.PostgreSQL
    // (MigrationController.cs:37-38). Under the SQLite processor this body is silently
    // skipped — NO SQLite branch is needed and none is added. Identifiers are
    // double-quoted so Postgres preserves case (unquoted folds `Chapters` → lowercase
    // and fails with 42P01); Migration 005/006 convention. Raw Execute.Sql is used
    // because the fluent Alter.Table(...).AlterColumn(...) form does not support the
    // `USING` clause required for the explicit UTC-anchored type cast. The column is
    // Nullable() and `NULL AT TIME ZONE 'UTC'` → `NULL`, so no null guard is needed.
    //
    // SCOPE: PARSE2-01 SC#1 names ONLY Chapter.FirstReleaseDate. Other `.AsDateTime()`
    // columns (LastSearchTime, History/Blocklist dates) are intentionally NOT swept —
    // the repo-wide sweep stays tracked as v1.1-09 step 2 (close-out plan surfaces it).
    //
    // No LogDbUpgrade() body — additive single-statement, Sonarr-canonical idiom
    // (cf. 004_*.cs:29). Sequential post-baseline (pre-v1 dev-migration policy ended
    // at Phase 21 close; v1.0.0 tag 2026-05-17). NEVER edits 001_mangarr_baseline.cs.
    // Succeeds Migration 006 (Phase 32).
    [Migration(7)]
    public class v1_2_chapter_firstreleasedate_timestamptz : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            IfDatabase(ProcessorIdConstants.PostgreSQL)
                .Execute.Sql("ALTER TABLE \"Chapters\" ALTER COLUMN \"FirstReleaseDate\" TYPE timestamptz USING \"FirstReleaseDate\" AT TIME ZONE 'UTC'");
        }
    }
}
