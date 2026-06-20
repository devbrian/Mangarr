using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // quick-260619-spc — extend the ImportListExclusions identity from the manga-ID
    // triplet (MangaDexId/MalId/AniListId — Migration 003, Phase 26 D-13) to a quad by
    // adding the MangaBakaId column, so exclusions key on the v1.3 default-primary anchor.
    //
    // Pure additive nullable column add (dialect-agnostic via FluentMigrator — SQLite +
    // Postgres). Zero seed (sonarr-consistency-audit Anti-pattern C floor — no
    // Insert.IntoTable). Fresh-DB no-op; mirrors the proven Migration 013 additive shape.
    //
    // NO UNIQUE index by deliberate sibling-shape parity: on the EXCLUSION table only
    // MangaDexId carries the Migration-003 UNIQUE-with-NULLs index (the Sonarr TvdbId
    // peer). MalId/AniListId have no index, and MangaBakaId mirrors those secondary
    // members — app-level dedup is handled by the delete-event handler's idempotency
    // scan, matching the existing pattern.
    //
    // Sequential post-baseline migration (post-v1.0.0 append-only policy): NEVER edits
    // 001_mangarr_baseline.cs. Succeeds Migration 016 (quick-260619-o5q — add_max_chapter_number).
    // [Migration(17)] is the new head. No LogDbUpgrade (main-db only).
    [Migration(17)]
    public class v1_3_add_importlist_exclusion_mangabaka_id : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("ImportListExclusions").AddColumn("MangaBakaId").AsInt32().Nullable();
        }
    }
}
