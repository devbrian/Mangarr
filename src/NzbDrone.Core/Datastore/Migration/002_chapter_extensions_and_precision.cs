using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Phase 2 schema delta — extends the Phase 1 manga-baseline `Chapters` and `Manga`
    // tables with the parser/metadata-source surface that Plans 02-03..02-10 consume.
    // All operations are additive or shape-preserving widens; no Phase 1 table is
    // recreated and 001_mangarr_baseline.cs is NOT edited (Pitfall 1; D-11 lock).
    //
    // Operations (per Phase 2 02-CONTEXT.md decisions):
    //   1. Chapters: 4 additive columns (D-11) — ChapterType / VolumeNumber /
    //      AbsoluteChapterNumber / IsSynthetic.
    //   2. Chapters: widen ChapterNumber DECIMAL(10,2) → DECIMAL(10,3) (D-12 — REVERSAL
    //      of Phase 1 D-09; scanlation filenames occasionally use 3-decimal forms).
    //   3. Postgres-only index rebuild: drop + recreate
    //      IX_Chapters_MangaId_ChapterNumber_TranslatedLanguage after the widen
    //      (Pitfall 3 — Postgres caches index column type; SQLite REAL is dialect-free
    //      and survives the widen without rebuild).
    //   4. Manga: drop JSON list columns MalIds/AniListIds (Phase 1 baseline; partial
    //      Sonarr migration 217 shape) and add singular int? MalId/AniListId per
    //      02-RESEARCH §Open Question 5 — manga has 1:1 MAL/AniList canonical mapping,
    //      not the n:m TV-side anime cross-references. Also adds three multi-axis
    //      cross-source resolver inputs: TotalChapterCount (D-17 synthesis fallback),
    //      PublicationYear + PrimaryAuthor (D-21 multi-axis confirm).
    //   5. MetadataSources: NEW ThingiProvider table (D-14, D-15) — distinct from the
    //      existing `Metadata` IMetadataConsumer table per Pitfall 2. Ships with
    //      IsPrimary column defaulted false; at-most-one invariant enforced in
    //      MetadataSourceFactory.SetPrimary (Plan 02-05), not the DB.
    //   6. ScheduledTasks: insert RefreshMangaCommand row with Interval=720 (12h cadence
    //      per D-18; mirrors Sonarr's RefreshSeriesCommand cadence).
    [Migration(2)]
    public class chapter_extensions_and_precision : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // 1. Additive Chapters columns (D-11).
            Alter.Table("Chapters")
                .AddColumn("ChapterType").AsString().NotNullable().WithDefaultValue("Regular")
                .AddColumn("VolumeNumber").AsInt32().Nullable()
                .AddColumn("AbsoluteChapterNumber").AsDecimal(10, 3).Nullable()
                .AddColumn("IsSynthetic").AsBoolean().NotNullable().WithDefaultValue(false);

            // 2. Widen ChapterNumber DECIMAL(10,2) → DECIMAL(10,3) (D-12 — REVERSAL of
            //    Phase 1 D-09).
            Alter.Column("ChapterNumber").OnTable("Chapters")
                .AsDecimal(10, 3).NotNullable();

            // 3. Postgres-only composite index rebuild after the widen (Pitfall 3).
            //    SQLite REAL ignores precision so the existing index survives unchanged;
            //    Postgres caches the column's numeric(10,2) type in the index and must
            //    drop+recreate after the type change.
            IfDatabase("postgres").Delegate(() =>
            {
                Execute.Sql("DROP INDEX IF EXISTS \"IX_Chapters_MangaId_ChapterNumber_TranslatedLanguage\"");
                Execute.Sql(
                    "CREATE INDEX \"IX_Chapters_MangaId_ChapterNumber_TranslatedLanguage\" " +
                    "ON \"Chapters\" (\"MangaId\" ASC, \"ChapterNumber\" ASC, \"TranslatedLanguage\" ASC)");
            });

            // 4. Manga — drop JSON list columns, add singular int? columns plus the
            //    three cross-source resolver inputs (02-RESEARCH §Open Question 5).
            Delete.Column("MalIds").FromTable("Manga");
            Delete.Column("AniListIds").FromTable("Manga");

            Alter.Table("Manga")
                .AddColumn("MalId").AsInt32().Nullable()
                .AddColumn("AniListId").AsInt32().Nullable()
                .AddColumn("TotalChapterCount").AsInt32().Nullable()      // D-17 synthesis fallback
                .AddColumn("PublicationYear").AsInt32().Nullable()        // D-21 multi-axis confirm
                .AddColumn("PrimaryAuthor").AsString().Nullable();        // D-21 multi-axis confirm

            // 5. MetadataSources — NEW ThingiProvider table (D-14, D-15). Distinct from
            //    the existing `Metadata` IMetadataConsumer table (Pitfall 2).
            Create.TableForModel("MetadataSources")
                .WithColumn("Name").AsString().NotNullable().Unique()
                .WithColumn("Implementation").AsString().NotNullable()
                .WithColumn("Settings").AsString().Nullable()
                .WithColumn("ConfigContract").AsString().Nullable()
                .WithColumn("Enable").AsBoolean().NotNullable().WithDefaultValue(true)
                .WithColumn("IsPrimary").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("Tags").AsString().Nullable();

            // 6. 12h scheduled-task row for RefreshMangaCommand (D-18). Mirrors Sonarr's
            //    series-refresh cadence; manual trigger lands in Plan 02-09 (developer
            //    endpoint) and Phase 7 (UI button).
            Insert.IntoTable("ScheduledTasks").Row(new
            {
                TypeName = "NzbDrone.Core.Manga.Commands.RefreshMangaCommand",
                Interval = 720.0,
                LastExecution = "2000-01-01 00:00:00",
            });
        }
    }
}
