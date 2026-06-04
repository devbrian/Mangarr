using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Manga-domain one-shot data-normalization migration per issue #320.
    //
    // WHY: the AddManga modal sent profileId == 0 (the int default) when no profile was picked and
    // none was the global default. `0` is a non-existent FK — profile rows start at id 1 — so it is
    // a sentinel every downstream consumer must special-case, and it was the upstream trigger for the
    // import wedge fixed in #318 (CustomFormatProfileService.Get(0) -> ModelNotFoundException).
    //
    // The canonical "use the seeded default" sentinel is NULL: every consumer resolves
    // `Manga.<X>ProfileId ?? Config.Default<X>ProfileId`, and both profile services seed
    // Config.Default*ProfileId to the Default profile on first run. The add-time fix
    // (AddMangaService.PrepareForAdd) now coerces 0 -> null so 0 never reaches persistence; this
    // one-shot migration normalizes the rows that were persisted with 0 BEFORE that fix, so existing
    // manga fall back to the seeded default profile at runtime instead of carrying the broken sentinel.
    //
    // TABLE/COLUMN NAMES are the SQL names (double-quoted, Postgres + SQLite compatible).
    [Migration(11)]
    public class normalize_manga_profile_ids : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Execute.Sql("UPDATE \"Manga\" SET \"TranslationProfileId\" = NULL WHERE \"TranslationProfileId\" = 0");
            Execute.Sql("UPDATE \"Manga\" SET \"CustomFormatProfileId\" = NULL WHERE \"CustomFormatProfileId\" = 0");
        }
    }
}
