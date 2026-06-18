using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // quick-260618-eqz — adds the user-owned alternative-title set.
    //
    // Sonarr divergence: NEW manga column for a user-curated alt-title list that
    // is SEPARATE from the metadata-sourced Manga.AlternativeTitles column (GH #118).
    // Where AlternativeTitles is refresh-OWNED (the metadata sources overwrite it on
    // every RefreshMangaCommand) and user-PUT-preserved, UserAlternativeTitles is the
    // inverse: user-PUT-OWNED and refresh-PRESERVED. The matching guard contract lives
    // in Manga.ApplyChanges (inverted relative to the AlternativeTitles guard); see
    // .planning/quick/260618-eqz-add-api-to-add-an-alternative-title-to-a/260618-eqz-CONTEXT.md.
    //
    // Nullable JSON-string column matching the AlternativeTitles baseline column shape
    // (001_mangarr_baseline.cs:527). Serialization rides the global
    // StringListConverter<List<string>> registration automatically (same as
    // AlternativeTitles / Genres) — no TableMapping change. The nullability is load-bearing:
    // a metadata-built Manga carries NULL here (MapManga never sets it), and the inverted
    // ApplyChanges guard uses NULL-vs-non-null to distinguish a metadata refresh (preserve)
    // from a user PUT (overwrite/clear).
    //
    // TABLE/COLUMN NAMES are the FluentMigrator names (Postgres + SQLite compatible).
    // Sequential post-baseline migration (post-v1.0.0 append-only policy); NEVER edits
    // 001_mangarr_baseline.cs. Succeeds Migration 014 (the IX_Manga_MangaBakaId head).
    [Migration(15)]
    public class v1_3_add_user_alternative_titles : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Manga").AddColumn("UserAlternativeTitles").AsString().Nullable();
        }
    }
}
