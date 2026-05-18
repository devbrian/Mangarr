using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Phase 24 v1.1 INSERTED 2026-05-17 — adds two columns to back the AutoTagging
    // restore-rebuild's manga-NEW specs:
    //   * Manga.Artist (TEXT NULL)         — D-03 AuthorArtistSpecification
    //   * Manga.Demographic (INTEGER NULL) — D-04 DemographicSpecification (MangaDemographic enum)
    //
    // Atomic with the AutoTagging subtree restore in Phase 24 Plan 24-02. Authored
    // under the post-Phase-21 sequential-migrations regime (pre-v1 dev-migration
    // policy ended at Phase 21 close — migrations 002+ are sequential, NEVER edits to
    // 001_mangarr_baseline.cs).
    //
    // No LogDbUpgrade() body needed — Sonarr-canonical idiom for simple ALTER TABLE.
    [Migration(2)]
    public class v1_1_manga_artist_demographic : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Manga").AddColumn("Artist").AsString().Nullable();
            Alter.Table("Manga").AddColumn("Demographic").AsInt32().Nullable();
        }
    }
}
