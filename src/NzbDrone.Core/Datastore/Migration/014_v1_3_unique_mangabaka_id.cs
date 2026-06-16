using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Completes the issue #213 concurrent-POST race protection for MangaBakaId.
    //
    // WHY: Manga.MangaDexId / MalId / AniListId carry UNIQUE indexes since the baseline
    // (001 IX_Manga_{MangaDexId,MalId,AniListId}) so two near-simultaneous POSTs with the
    // same external ID can't both insert. MangaBakaId was added unconstrained (Migration 012)
    // and only gained an app-level dedup guard (AddMangaService.PrepareForAdd via
    // FindByMangaBakaId) in the same change that made MangaBakaId a valid standalone add
    // anchor on POST /api/v5/manga. App-level FindBy*Id alone cannot close a TOCTOU race —
    // both POSTs pass the in-memory check before either insert commits. This migration adds
    // the missing IX_Manga_MangaBakaId UNIQUE index so the DB enforces first-wins, and the
    // MangaController.AddManga catch-blocks (extended for Manga.MangaBakaId / IX_Manga_MangaBakaId)
    // map the surfaced constraint violation to HTTP 409 Conflict — full parity with the big-3.
    //
    // DEFENSIVE PRE-DEDUP: unlike the big-3 (UNIQUE from table creation), MangaBakaId has been
    // populated WITHOUT a uniqueness guarantee since Migration 012, so an existing DB could
    // already hold duplicate values that would make a bare Create.Index(...).Unique() fail at
    // startup. Null the MangaBakaId of every row EXCEPT the lowest-Id one per duplicate value
    // first (ANSI NULL is distinct, so the UNIQUE index then builds). Any such loser row predates
    // the app-level dedup and therefore still carries a big-3 anchor (the pre-fix PostValidator
    // required one of MangaDex/MAL/AniList), so nulling its MangaBakaId never leaves it unanchored.
    // On a clean DB (no duplicates) the UPDATE is a no-op.
    //
    // TABLE/COLUMN NAMES are the SQL names (double-quoted, Postgres + SQLite compatible) per the
    // Migration 011 convention. Sequential post-baseline migration (post-v1.0.0 append-only policy);
    // NEVER edits 001_mangarr_baseline.cs. Succeeds Migration 013.
    [Migration(14)]
    public class v1_3_unique_mangabaka_id : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Execute.Sql(
                "UPDATE \"Manga\" SET \"MangaBakaId\" = NULL " +
                "WHERE \"MangaBakaId\" IS NOT NULL " +
                "AND \"Id\" NOT IN (" +
                "SELECT MIN(\"Id\") FROM \"Manga\" WHERE \"MangaBakaId\" IS NOT NULL GROUP BY \"MangaBakaId\")");

            Create.Index("IX_Manga_MangaBakaId").OnTable("Manga")
                .OnColumn("MangaBakaId").Ascending()
                .WithOptions().Unique();
        }
    }
}
