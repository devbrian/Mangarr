using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Phase 26 v1.1 INSERTED 2026-05-19 — combined atomic cluster (Plan 26-03 per
    // locked Phase 24 D-01 close-out 2026-05-17):
    //   IL-01:  ImportLists.QualityProfileId  → TranslationProfileId + CustomFormatProfileId
    //   IL-01:  ImportListItems.ImdbId        → MangaDexId + MalId + AniListId
    //                                          (TvdbId was already stripped in Phase 15 D-22
    //                                           per 001_mangarr_baseline.cs:165 inline comment)
    //   IL-01:  ImportListExclusions.TvdbId   → MangaDexId + MalId + AniListId
    //                                          + composite-unique index on MangaDexId
    //                                          (NULL-tolerant per SQLite UNIQUE-with-NULLs
    //                                           semantics — RESEARCH §Q4; permits AniList-only
    //                                           and MAL-only exclusions to coexist)
    //   DP-01:  DelayProfiles drops EnableUsenet + EnableTorrent + UsenetDelay + TorrentDelay
    //           (completes the Phase 15 D-22 omission per Phase 23 close-out forward-schedule)
    //
    // Sequential post-baseline (pre-v1 dev-migration policy ended at Phase 21 close;
    // v1.0.0 tag 2026-05-17). NEVER edits 001_mangarr_baseline.cs. Succeeds Migration 002
    // (Phase 24 — Manga.Artist + Manga.Demographic).
    //
    // Pitfall 1: the DDL ships atomic with the DelayProfile entity prop drop +
    // DelayProfileService.Seed payload trim + DelayProfileResourceMapper PHASE-23 BRIDGE
    // marker deletion + caller fixtures — partial commit breaks the runtime seeder
    // (Dapper "no such column" on next boot).
    //
    // No LogDbUpgrade() body needed — Sonarr-canonical idiom for ALTER TABLE-only deltas.
    [Migration(3)]
    public class importlist_substrate_delayprofile_trim : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // ─── ImportLists family reshape (IL-01) ─────────────────────────────
            // QualityProfileId → TranslationProfileId + CustomFormatProfileId
            Alter.Table("ImportLists").AddColumn("TranslationProfileId").AsInt32().Nullable();
            Alter.Table("ImportLists").AddColumn("CustomFormatProfileId").AsInt32().Nullable();
            Delete.Column("QualityProfileId").FromTable("ImportLists");

            // Phase 26 Plan 26-04 (IL-02) — TV-named SearchForMissingEpisodes column
            // renamed to manga-shape SearchForMissingChapters so ImportListDefinition's
            // POCO field matches the DB column 1:1 (Sonarr-canonical name; manga peer).
            Rename.Column("SearchForMissingEpisodes")
                  .OnTable("ImportLists")
                  .To("SearchForMissingChapters");

            // ImportListItems TVDB/IMDB → manga-ID triplet.
            // Note: ImportListItems.TvdbId was already stripped in Phase 15 D-22 per
            // 001_mangarr_baseline.cs:165 inline comment — only ImdbId remains here
            // pre-Migration-003. Do NOT emit Delete.Column for TvdbId on ImportListItems
            // (FluentMigrator throws on unknown column).
            Alter.Table("ImportListItems").AddColumn("MangaDexId").AsString().Nullable();
            Alter.Table("ImportListItems").AddColumn("MalId").AsInt32().Nullable();
            Alter.Table("ImportListItems").AddColumn("AniListId").AsInt32().Nullable();
            Delete.Column("ImdbId").FromTable("ImportListItems");

            // ImportListExclusions reshape — TvdbId is the only non-Title column per
            // 001_mangarr_baseline.cs:467-469. Drops the inline table-level UNIQUE
            // alongside the column (FluentMigrator drops dependent constraints).
            Alter.Table("ImportListExclusions").AddColumn("MangaDexId").AsString().Nullable();
            Alter.Table("ImportListExclusions").AddColumn("MalId").AsInt32().Nullable();
            Alter.Table("ImportListExclusions").AddColumn("AniListId").AsInt32().Nullable();
            Delete.Column("TvdbId").FromTable("ImportListExclusions");

            // SQLite UNIQUE-with-NULLs semantics permit multiple NULL MangaDexId rows
            // (AniList-only or MAL-only exclusions); RESEARCH §Q4 cites SQLite documentation.
            Create.Index("IX_ImportListExclusions_MangaDexId")
                  .OnTable("ImportListExclusions")
                  .OnColumn("MangaDexId").Ascending()
                  .WithOptions().Unique();

            // ─── DelayProfile 4-column drop (DP-01) ─────────────────────────────
            Delete.Column("EnableUsenet").FromTable("DelayProfiles");
            Delete.Column("EnableTorrent").FromTable("DelayProfiles");
            Delete.Column("UsenetDelay").FromTable("DelayProfiles");
            Delete.Column("TorrentDelay").FromTable("DelayProfiles");
        }
    }
}
