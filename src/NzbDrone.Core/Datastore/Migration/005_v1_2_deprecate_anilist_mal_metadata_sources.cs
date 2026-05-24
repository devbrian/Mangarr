using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Phase 31 v1.2 INSERTED 2026-05-24 — IL2-01 D-03 reframe atomic cluster.
    //
    // ⚠ STUB ONLY (Task 1 TDD RED gate placeholder). The DELETE SQL body is filled in
    // by Plan 31-01 Task 2 (commit feat(31-01): Migration 005 deprecate AniList+MAL
    // MetadataSources + IsDeprecated virtual). This stub exists so the Task 1 RED
    // fixtures (Migration005Fixture in particular) can compile against MigrationTest<T>
    // before Task 2 ships the DELETE SQL. Per Rule 3 deviation in execute-plan.md —
    // TDD RED gate requires the class to exist at compile time for MigrationTest<T>.
    //
    // After Task 2 lands, the body becomes:
    //   D-02: Hide AniList + MAL from Settings → MetadataSources Add picker (filter at
    //         schema-emit site — see MetadataSourceController.cs filter shape).
    //   D-03: DELETE existing AniListMetadataSource + MyAnimeListMetadataSource rows
    //         from MetadataSources table. Atomic with the schema-emit filter so users
    //         who had AniList or MAL as primary metadata source get MangaDex auto-seeded
    //         by MetadataSourceFactory.InitializeProviders on next boot.
    //
    // Sequential post-baseline (pre-v1 dev-migration policy ended at Phase 21 close;
    // v1.0.0 tag 2026-05-17). NEVER edits 001_mangarr_baseline.cs. Succeeds Migration 004
    // (Phase 30 — ChapterFile.MediaInfo + ComicInfoMetadata seed).
    [Migration(5)]
    public class v1_2_deprecate_anilist_mal_metadata_sources : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Task 1 STUB — body filled by Task 2.
        }
    }
}
