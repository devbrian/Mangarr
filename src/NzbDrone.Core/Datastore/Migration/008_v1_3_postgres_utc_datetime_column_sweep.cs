using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // v1.3 — Issue #270 (v1.1-09 step 2): repo-wide UTC-DateTime column sweep on
    // the Postgres backend. Generalizes Migration 007 (which fixed only
    // Chapters.FirstReleaseDate per PARSE2-01 SC#1's deliberate single-column scope)
    // to EVERY .AsDateTime() column in 001_mangarr_baseline.cs that carries a
    // DateTimeKind.Utc instant.
    //
    // WHY: every .AsDateTime() column maps to Postgres `timestamp without time zone`.
    // The global DapperUtcConverter (UtcConverter.cs, registered for both `DateTime`
    // and `DateTime?` at TableMapping.cs:295-296) does `ToUniversalTime()` on write and
    // `SpecifyKind(..., DateTimeKind.Utc)` on read, so every value flowing through Dapper
    // is a UTC instant. Under modern Npgsql (no EnableLegacyTimestampBehavior switch is
    // set anywhere in the tree) a `DateTimeKind.Utc` value cannot cleanly round-trip
    // through a TZ-less column — it offset-shifts on write/read. `timestamptz` is the
    // type Npgsql expects for a UTC instant; switching eliminates the shift. This is the
    // identical defect Migration 007 fixed for one column, applied to the rest.
    //
    // SQLite stores DateTime as text and round-trips identically, so the defect is
    // Postgres-only and was latent for these columns. The IfDatabase(PostgreSQL) guard
    // skips this body entirely under the SQLite processor (MigrationController.cs:37-38
    // maps DatabaseType → ProcessorId) — NO SQLite branch is needed and none is added.
    //
    // Idiom mirrors Migration 007 verbatim: `ALTER COLUMN ... TYPE timestamptz USING
    // "Col" AT TIME ZONE 'UTC'`. The USING clause anchors the existing TZ-less wall-clock
    // (which the writer stored as UTC) to UTC, producing the correct timestamptz with no
    // data shift. Identifiers are double-quoted so Postgres preserves case (unquoted folds
    // `Manga` → lowercase and fails 42P01); Migration 005/006/007 convention. Raw
    // Execute.Sql is used because the fluent Alter.Table(...).AlterColumn(...) form does
    // not support the `USING` clause. Nullable columns: `NULL AT TIME ZONE 'UTC'` → NULL,
    // so no null guard is needed.
    //
    // SCOPE — what is swept (39 columns; the Dapper-round-tripped UTC-instant class):
    //   ScheduledTasks.LastExecution/LastStartTime, ImportListItems.ReleaseDate,
    //   History.Date, Blocklist.Date/PublishedDate,
    //   IndexerStatus.InitialFailure/MostRecentFailure/DisabledTill/CookiesExpirationDate,
    //   IndexerSourceStatus.InitialFailure/MostRecentFailure/DisabledTill,
    //   ChapterDownloadState.ManifestExpiresAt/RetentionUntil/CreatedAt/UpdatedAt,
    //   DownloadClientStatus.InitialFailure/MostRecentFailure/DisabledTill,
    //   ImportListStatus.InitialFailure/MostRecentFailure/DisabledTill/LastInfoSync,
    //   NotificationStatus.InitialFailure/MostRecentFailure/DisabledTill,
    //   PendingReleases.Added, MangaPendingReleases.Added,
    //   Commands.QueuedAt/StartedAt/EndedAt, DownloadHistory.Date,
    //   Manga.Added/LastInfoSync, Chapters.LastSearchTime, ChapterHistory.Date,
    //   MangaBlocklist.Date, ChapterFiles.DateAdded, Indexers.LastRssSync.
    //   Plus (LogDbUpgrade) UpdateHistory.Date — same Dapper defect class, in logs.db
    //   (which is ALSO Postgres when Postgres is configured — ConnectionStringFactory.cs:31-32).
    //
    // SCOPE — deliberately NOT swept:
    //   * Chapters.FirstReleaseDate — already altered to timestamptz by Migration 007.
    //   * Logs.Time — NOT a Dapper-path column. It is written by NLog's DatabaseTarget
    //     via a raw `NpgsqlParameter("Time", DbType.DateTime)` whose value is
    //     pre-converted with `.ToUniversalTime()` (DatabaseTarget.cs:122). DbType.DateTime
    //     pins the parameter to `timestamp without time zone`, so a TZ-less column is the
    //     CORRECT match for that write path; altering it to timestamptz would mismatch the
    //     explicit parameter type. Excluded by design, not deferred.
    //
    // Sequential post-baseline (post-v1.0.0 — append-only migration policy). Succeeds
    // Migration 007. NEVER edits 001_mangarr_baseline.cs.
    [Migration(8)]
    public class v1_3_postgres_utc_datetime_column_sweep : NzbDroneMigrationBase
    {
        private static readonly (string Table, string Column)[] MainDbColumns =
        {
            ("ScheduledTasks", "LastExecution"),
            ("ScheduledTasks", "LastStartTime"),
            ("ImportListItems", "ReleaseDate"),
            ("History", "Date"),
            ("Blocklist", "Date"),
            ("Blocklist", "PublishedDate"),
            ("IndexerStatus", "InitialFailure"),
            ("IndexerStatus", "MostRecentFailure"),
            ("IndexerStatus", "DisabledTill"),
            ("IndexerStatus", "CookiesExpirationDate"),
            ("IndexerSourceStatus", "InitialFailure"),
            ("IndexerSourceStatus", "MostRecentFailure"),
            ("IndexerSourceStatus", "DisabledTill"),
            ("ChapterDownloadState", "ManifestExpiresAt"),
            ("ChapterDownloadState", "RetentionUntil"),
            ("ChapterDownloadState", "CreatedAt"),
            ("ChapterDownloadState", "UpdatedAt"),
            ("DownloadClientStatus", "InitialFailure"),
            ("DownloadClientStatus", "MostRecentFailure"),
            ("DownloadClientStatus", "DisabledTill"),
            ("ImportListStatus", "InitialFailure"),
            ("ImportListStatus", "MostRecentFailure"),
            ("ImportListStatus", "DisabledTill"),
            ("ImportListStatus", "LastInfoSync"),
            ("NotificationStatus", "InitialFailure"),
            ("NotificationStatus", "MostRecentFailure"),
            ("NotificationStatus", "DisabledTill"),
            ("PendingReleases", "Added"),
            ("MangaPendingReleases", "Added"),
            ("Commands", "QueuedAt"),
            ("Commands", "StartedAt"),
            ("Commands", "EndedAt"),
            ("DownloadHistory", "Date"),
            ("Manga", "Added"),
            ("Manga", "LastInfoSync"),
            ("Chapters", "LastSearchTime"),
            ("ChapterHistory", "Date"),
            ("MangaBlocklist", "Date"),
            ("ChapterFiles", "DateAdded"),
            ("Indexers", "LastRssSync"),
        };

        protected override void MainDbUpgrade()
        {
            foreach (var (table, column) in MainDbColumns)
            {
                IfDatabase(ProcessorIdConstants.PostgreSQL)
                    .Execute.Sql($"ALTER TABLE \"{table}\" ALTER COLUMN \"{column}\" TYPE timestamptz USING \"{column}\" AT TIME ZONE 'UTC'");
            }
        }

        protected override void LogDbUpgrade()
        {
            // logs.db is Postgres when Postgres is configured (ConnectionStringFactory.cs:31-32).
            // UpdateHistory is a Dapper repository (UpdateHistoryRepository : BasicRepository),
            // so UpdateHistory.Date is the same UTC-round-trip defect class as the main-DB columns.
            IfDatabase(ProcessorIdConstants.PostgreSQL)
                .Execute.Sql("ALTER TABLE \"UpdateHistory\" ALTER COLUMN \"Date\" TYPE timestamptz USING \"Date\" AT TIME ZONE 'UTC'");
        }
    }
}
