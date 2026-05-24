using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Phase 31 v1.2 INSERTED 2026-05-24 — IL2-01 D-03 reframe atomic cluster:
    //   D-01: MangaDex is the sole user-facing primary metadata source per Phase 31
    //         reframe ("I don't care about other metadata sources besides mangadex
    //         for metadata"). AniList + MAL are deprecated as primary candidates.
    //   D-02: AniList + MAL hidden from the Settings → MetadataSources Add picker
    //         by the V5 controller's GetTemplates `new` override reading the
    //         IsDeprecated virtual on each provider. This migration is the
    //         persisted-row counterpart that removes existing AniList + MAL
    //         MetadataSourceDefinition rows so the user-visible state matches the
    //         schema-emit filter end-to-end.
    //   D-03: DELETE existing AniListMetadataSource + MyAnimeListMetadataSource
    //         rows from MetadataSources table (delete-via-migration shape picked
    //         per RESEARCH §Recommended Shapes D-03 — smallest blast radius).
    //
    // Cross-dialect SQL contract: identifiers double-quoted so Postgres preserves
    // case (without quotes Postgres folds `MetadataSources` -> `metadatasources`
    // and fails with `42P01: relation "metadatasources" does not exist`). SQLite
    // accepts double-quoted identifiers identically.
    //
    // Table name is "MetadataSources" (plural, Phase 2 D-14 IMetadataSource
    // ThingiProvider) — NOT "Metadata" (singular, Sonarr-canonical IMetadataConsumer
    // ThingiProvider per Phase 15 D-02/D-03). See 001_mangarr_baseline.cs:141-148
    // for the MetadataSources table schema (and :129-134 for the disambiguator
    // Metadata table).
    //
    // Idempotency: DELETE WHERE IN (...) matches zero rows on re-execute (fresh DB
    // users without prior AniList/MAL rows) — FluentMigrator's transactional
    // semantics keep the migration safe to replay.
    //
    // MetadataSourceFactory.InitializeProviders auto-seed safety net (see
    // MetadataSourceFactory.cs:92-110) runs AFTER this migration on next boot:
    // when All().Any() returns false (post-DELETE on a DB that had ONLY AniList +
    // MAL rows), the factory auto-seeds the MangaDex DefaultDefinition
    // (DefaultIsPrimary=true) so _metadataSourceFactory.GetPrimary() never throws
    // on user databases coming through Migration 005.
    //
    // Pre-v1 dev-migration policy ENDED at Phase 21 close (v1.0.0 tag 2026-05-17).
    // Sequential post-baseline migration: NEVER edits 001_mangarr_baseline.cs.
    // Succeeds Migration 004 (Phase 30 — ChapterFile.MediaInfo + ComicInfoMetadata
    // seed).
    //
    // D-01 reframe rationale: AniList + MAL MapManga methods stay in code
    // (invoked via cross-source SearchForNewManga from ImportListSyncService.cs:243);
    // only the user-facing Add picker entry + persisted MetadataSourceDefinition
    // rows are removed.
    [Migration(5)]
    public class v1_2_deprecate_anilist_mal_metadata_sources : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Execute.WithConnection((connection, transaction) =>
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = @"DELETE FROM ""MetadataSources""
                                    WHERE ""Implementation"" IN ('AniListMetadataSource', 'MyAnimeListMetadataSource')";
                cmd.ExecuteNonQuery();
            });
        }
    }
}
