using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // quick-260608-l2e — pull in the remaining MangaBaka cross-source ids from the series
    // `source` block onto the Manga table. Phase 41 mapped only source.anilist.id /
    // source.my_anime_list.id; this adds the other five direct ids.
    //
    // Pure additive column adds (dialect-agnostic via FluentMigrator — SQLite + Postgres):
    //   • KitsuId / AnimeNewsNetworkId / ShikimoriId  -> AsInt32().Nullable()   (RAW INTEGER ids)
    //   • AnimePlanetId / MangaUpdatesId              -> AsString().Nullable()  (slug / base36 token)
    //
    // No seeding (sonarr-consistency-audit Anti-pattern C — zero Insert.IntoTable). These five
    // columns mirror the existing canonical cross-source id shape (MangaDexId/MalId/AniListId/
    // MangaBakaId) and are immutable post-add (Manga.ApplyChanges omit).
    //
    // Sequential post-baseline migration (post-v1.0.0 append-only policy): NEVER edits
    // 001_mangarr_baseline.cs. Succeeds Migration 012 (Phase 41 — add_mangabaka_metadata_source).
    [Migration(13)]
    public class v1_3_add_mangabaka_cross_source_ids : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Manga").AddColumn("KitsuId").AsInt32().Nullable();
            Alter.Table("Manga").AddColumn("AnimeNewsNetworkId").AsInt32().Nullable();
            Alter.Table("Manga").AddColumn("ShikimoriId").AsInt32().Nullable();
            Alter.Table("Manga").AddColumn("AnimePlanetId").AsString().Nullable();
            Alter.Table("Manga").AddColumn("MangaUpdatesId").AsString().Nullable();
        }
    }
}
