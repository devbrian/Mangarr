using System.Collections.Generic;
using System.Data;
using System.Text.Json.Nodes;
using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Phase 32 v1.2 INSERTED 2026-05-25 — CORR-04 follow-up (PR #265 Codex P1).
    //
    // Phase 32 Plan 32-04 deleted the dead-bound `LanguageSpecification` class
    // outright. `CustomFormatSpecificationListConverter.Read`
    // (src/NzbDrone.Core/Datastore/Converters/CustomFormatSpecificationConverter.cs:46)
    // resolves each persisted spec's runtime Type with
    //   Type.GetType($"NzbDrone.Core.CustomFormats.{typename}, Mangarr.Core", true)
    // where the third arg `throwOnError=true` makes any unknown typename throw a
    // TypeLoadException, breaking startup deserialization of the CustomFormats
    // table for any user whose `Specifications` JSON array still contains a
    // {"Type":"LanguageSpecification",...} wrapper.
    //
    // `LanguageSpecification` was registered as a valid `ICustomFormatSpecification`
    // type in shipped v1.0.0 + v1.1.0 — its `AppliesTo=Series` hid it from the
    // manga CF UI per Phase 15 D-10, but direct API POSTs / imported Sonarr CFs
    // could persist it. This migration scrubs every such wrapper from each
    // `CustomFormats.Specifications` JSON array.
    //
    // Cross-dialect: read + parse + write via raw ADO.NET so SQLite + Postgres
    // share one code path. Identifiers double-quoted per Migration 005 pattern
    // (Postgres folds unquoted identifiers to lowercase and fails with
    // `42P01: relation "customformats" does not exist`; SQLite accepts the
    // quoted form identically).
    [Migration(6)]
    public class v1_2_scrub_dead_language_specification_from_custom_formats : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Execute.WithConnection((connection, transaction) =>
            {
                var updates = new List<(int Id, string Specifications)>();

                using (var selectCmd = connection.CreateCommand())
                {
                    selectCmd.Transaction = transaction;
                    selectCmd.CommandText = @"SELECT ""Id"", ""Specifications"" FROM ""CustomFormats""";

                    using var reader = selectCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var id = reader.GetInt32(0);
                        var specs = reader.GetString(1);

                        // Fast filter: skip rows that don't even mention the dead type.
                        if (specs.IndexOf("\"LanguageSpecification\"", System.StringComparison.Ordinal) < 0)
                        {
                            continue;
                        }

                        if (JsonNode.Parse(specs) is not JsonArray array)
                        {
                            continue;
                        }

                        var beforeCount = array.Count;
                        for (var i = array.Count - 1; i >= 0; i--)
                        {
                            var typeNode = array[i]?["Type"];
                            if (typeNode != null && typeNode.GetValue<string>() == "LanguageSpecification")
                            {
                                array.RemoveAt(i);
                            }
                        }

                        if (array.Count != beforeCount)
                        {
                            updates.Add((id, array.ToJsonString()));
                        }
                    }
                }

                foreach (var (id, specifications) in updates)
                {
                    using var updateCmd = connection.CreateCommand();
                    updateCmd.Transaction = transaction;
                    updateCmd.CommandText = @"UPDATE ""CustomFormats""
                                              SET ""Specifications"" = @specs
                                              WHERE ""Id"" = @id";

                    var specsParam = updateCmd.CreateParameter();
                    specsParam.ParameterName = "@specs";
                    specsParam.DbType = DbType.String;
                    specsParam.Value = specifications;
                    updateCmd.Parameters.Add(specsParam);

                    var idParam = updateCmd.CreateParameter();
                    idParam.ParameterName = "@id";
                    idParam.DbType = DbType.Int32;
                    idParam.Value = id;
                    updateCmd.Parameters.Add(idParam);

                    updateCmd.ExecuteNonQuery();
                }
            });
        }
    }
}
