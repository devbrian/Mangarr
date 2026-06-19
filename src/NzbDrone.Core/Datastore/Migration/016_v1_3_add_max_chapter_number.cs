using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // quick-260619-o5q — adds the user-owned per-manga synthesis ceiling.
    //
    // Sonarr divergence: NEW manga column with NO Sonarr peer. TheTVDB is the
    // authoritative source on a series' canonical episode count, so Sonarr never needs
    // a user-supplied ceiling. Manga has no such authority — the external gateway can
    // surface mislabeled releases (or a metadata source can over-report), spawning
    // hundreds of phantom "Missing" rows. MaxChapterNumber gives the user a manual,
    // deliberate hard ceiling for titles whose real chapter count they know. The
    // automatic density-floor guard (ChapterDensityCut) is the heuristic peer; this is
    // the manual override.
    //
    // Nullable is load-bearing: NULL (default) == no cap == synthesis behaves byte-for-
    // byte as it does today; only a non-null value engages the clamp in
    // ChapterSynthesisService.SynthesizeFromDecisions. Unlike the immutable cross-source
    // IDs, MaxChapterNumber IS user-mutable (round-tripped through MangaResource +
    // Manga.ApplyChanges) but is NOT metadata-owned — a metadata refresh must never write
    // it (the inverted ApplyChanges guard, mirroring UserAlternativeTitles, preserves it).
    //
    // int32 (NOT decimal): the synthesis backfill loop is whole-number only — an int cap
    // matches the [1..maxWhole] loop unit and reads cleanest.
    //
    // Sequential post-baseline migration (post-v1.0.0 append-only policy); NEVER edits
    // 001_mangarr_baseline.cs. Succeeds Migration 015 (the prior head — the
    // UserAlternativeTitles column). Dialect-agnostic via FluentMigrator (SQLite +
    // Postgres). [Migration(16)] is the new head.
    [Migration(16)]
    public class v1_3_add_max_chapter_number : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Manga").AddColumn("MaxChapterNumber").AsInt32().Nullable();
        }
    }
}
