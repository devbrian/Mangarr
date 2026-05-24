using System;
using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Phase 30 v1.2 INSERTED 2026-05-23 — atomic cluster (Plan 30-05 per D-08):
    //   II2-03: ChapterFile.MediaInfo JSON column (additive nullable)
    //   II2-02: ComicInfoMetadata MetadataDefinition seed row IF Config.MetadataFormats
    //           contained "comicinfo" (D-03 preserves user state)
    //
    // R-10 RESOLVED: Metadata table ALREADY EXISTS in 001_mangarr_baseline.cs:129-134
    // (Sonarr-canonical IMetadataConsumer ThingiProvider; preserved per Phase 15 D-02/D-03).
    // This migration is ALTER + conditional INSERT only; no table-creation call is needed.
    //
    // D-11 atomicity: this migration MUST land BEFORE Plan 30-04's MetadataFactory
    // runtime code reaches the InitializeProviders -> _providerRepository.InsertMany
    // line on app start. Both plans live on the same branch; merge order Plan 30-05
    // BEFORE Plan 30-04 enforces the runtime dependency order.
    //
    // Sequential post-baseline (pre-v1 dev-migration policy ended at Phase 21 close;
    // v1.0.0 tag 2026-05-17). NEVER edits 001_mangarr_baseline.cs. Succeeds Migration 003
    // (Phase 26 — ImportLists substrate + DelayProfile trim).
    //
    // R-11 mitigation: Metadata table baseline shape (Enable, Name, Implementation,
    // Settings, ConfigContract — 5 columns; no Tags column) is line-anchored at plan time.
    // INSERT below supplies exactly those columns in baseline-declaration order.
    //
    // No LogDbUpgrade() body needed — Sonarr-canonical idiom for additive ALTER + INSERT.
    [Migration(4)]
    public class chapterfile_mediainfo_metadata_seed : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // ─── II2-03: ChapterFile.MediaInfo JSON column ─────────────────────
            // SQLite has no native JSON type; stored as TEXT and round-tripped via
            // the EmbeddedDocumentConverter<ChapterMediaInfo> Dapper TypeHandler
            // (registration in TableMapping.cs by Plan 30-05 Task 1).
            Alter.Table("ChapterFiles").AddColumn("MediaInfo").AsString().Nullable();

            // ─── II2-02: ComicInfoMetadata seed (D-03 preserve user state) ─────
            // Read Config.MetadataFormats — ConfigService.MetadataFormats default = ["comicinfo"]
            // when the Config row is absent (Phase 4 D-14). If the formats string contains
            // "comicinfo" (case-insensitive), seed the MetadataDefinition row enabled so
            // Plan 30-04's archiver-flip (DryIoc auto-discovery -> _metadataFactory.Enabled())
            // continues writing ComicInfo.xml into new CBZs without user reconfiguration.
            //
            // INSERT uses the LOCKED column list from 001_mangarr_baseline.cs:129-134
            // (5 columns; no Tags). Column order matches baseline declaration order exactly.
            //
            // Cross-dialect SQL contract: ALL identifiers are double-quoted so Postgres
            // preserves case (without quotes Postgres folds `Config` -> `config` and
            // fails with `42P01: relation "config" does not exist`). SQLite accepts
            // double-quoted identifiers identically. Boolean literal uses TRUE
            // (Postgres-canonical, also valid in SQLite 3.23+ as a 1/0 alias).
            Execute.WithConnection((connection, transaction) =>
            {
                string rawFormats;
                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = "SELECT \"Value\" FROM \"Config\" WHERE \"Key\" = 'metadataformats'";
                    rawFormats = cmd.ExecuteScalar() as string;
                }

                // Default per ConfigService.MetadataFormats getter when row absent.
                var formats = string.IsNullOrEmpty(rawFormats) ? "[\"comicinfo\"]" : rawFormats;

                if (formats.IndexOf("comicinfo", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    using var ins = connection.CreateCommand();
                    ins.Transaction = transaction;
                    ins.CommandText = @"INSERT INTO ""Metadata"" (""Enable"", ""Name"", ""Implementation"", ""Settings"", ""ConfigContract"")
                                        VALUES (TRUE, 'ComicInfo', 'ComicInfoMetadata', '{}', 'ComicInfoMetadataSettings')";
                    ins.ExecuteNonQuery();
                }
            });
        }
    }
}
