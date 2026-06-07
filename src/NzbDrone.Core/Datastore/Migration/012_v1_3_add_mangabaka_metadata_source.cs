using System.Data;
using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Phase 41 (mangabaka-metadata-provider) — persistence + seeding foundation for the
    // MangaBaka metadata provider. Two independent statements:
    //
    //   1. ADD the nullable Manga.MangaBakaId int column (D-03). MangaBaka ids are integers
    //      (mirrors MalId/AniListId), so this is AsInt32().Nullable() — NOT a Guid column.
    //
    //   2. DEMOTE the MangaDex-as-primary row only (D-01 guard — "demote MangaDex only when
    //      MangaDex is the current primary"). This migration does NOT promote or INSERT a
    //      MangaBaka row: MetadataSourceFactory.InitializeProviders backfills + promotes the
    //      MangaBaka primary on next boot (Open Q2 resolution — see MetadataSourceFactory.cs).
    //      Replicating the provider's default Settings-JSON serialization in raw SQL here is
    //      fragile and drifts when the Settings shape changes; the factory path keeps a single
    //      serialization source-of-truth. So the migration only flips an existing flag; the
    //      factory creates the new row.
    //
    // Cross-dialect SQL contract (copied verbatim from Migration 005 — the WR-02 idiom):
    //   • Boolean comparisons use TYPED DbType.Boolean parameters (@primary = true for the
    //     WHERE, @notPrimary = false for the SET) — NEVER an integer-literal comparison.
    //     PostgreSQL rejects a `boolean = integer` comparison with `42883: operator does not
    //     exist: boolean = integer`;
    //     Npgsql binds the typed parameter as native `boolean` and Microsoft.Data.Sqlite binds
    //     it as `INTEGER 1/0`, so both dialects evaluate the comparison correctly.
    //   • Identifiers are double-quoted ("MetadataSources", "IsPrimary", "Implementation",
    //     "Id") so Postgres preserves case (without quotes it folds to lowercase and fails with
    //     `42P01: relation "metadatasources" does not exist`). SQLite accepts them identically.
    //   • Table name is "MetadataSources" (plural — the Phase 2 D-14 IMetadataSource
    //     ThingiProvider table) — NEVER "MetadataSourceDefinition".
    //
    // The guard: `Implementation = 'MangaDexMetadataSource' AND "IsPrimary" = @primary` matches
    // the MangaDex row ONLY when it is the current primary. If a non-MangaDex provider is the
    // explicit primary (the user chose it), the MangaDex row is not primary, zero rows match,
    // and the explicit primary is left untouched (guard no-op). MIN("Id") defends the
    // single-row scope against duplicate MangaDex rows (botched re-adds / manual SQL edits),
    // matching the MetadataSourceFactory.SetPrimary earliest-by-id tie-breaker convention.
    //
    // Idempotency: re-running matches zero rows once MangaDex is already non-primary — safe to
    // replay under FluentMigrator's transactional semantics.
    //
    // Sequential post-baseline migration (post-v1.0.0 append-only policy): NEVER edits
    // 001_mangarr_baseline.cs. Succeeds Migration 011 (issue #320 — normalize_manga_profile_ids).
    [Migration(12)]
    public class v1_3_add_mangabaka_metadata_source : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Manga").AddColumn("MangaBakaId").AsInt32().Nullable();

            Execute.WithConnection((connection, transaction) =>
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = @"UPDATE ""MetadataSources""
                                    SET ""IsPrimary"" = @notPrimary
                                    WHERE ""Id"" = (SELECT MIN(""Id"") FROM ""MetadataSources"" WHERE ""Implementation"" = 'MangaDexMetadataSource')
                                      AND ""IsPrimary"" = @primary";

                var primaryParam = cmd.CreateParameter();
                primaryParam.ParameterName = "@primary";
                primaryParam.DbType = DbType.Boolean;
                primaryParam.Value = true;
                cmd.Parameters.Add(primaryParam);

                var notPrimaryParam = cmd.CreateParameter();
                notPrimaryParam.ParameterName = "@notPrimary";
                notPrimaryParam.DbType = DbType.Boolean;
                notPrimaryParam.Value = false;
                cmd.Parameters.Add(notPrimaryParam);

                cmd.ExecuteNonQuery();
            });
        }
    }
}
