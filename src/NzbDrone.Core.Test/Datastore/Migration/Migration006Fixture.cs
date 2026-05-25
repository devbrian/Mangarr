using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    // Phase 32 v1.2 INSERTED 2026-05-25 — pins Migration 006 behavior (CORR-04 follow-up,
    // PR #265 Codex P1).
    //
    // Migration 006 scrubs {"Type":"LanguageSpecification",...} wrapper objects out of
    // each CustomFormats.Specifications JSON array so post-Phase-32 startup doesn't
    // throw TypeLoadException at CustomFormatSpecificationListConverter:46.
    //
    // Mirrors the SqliteOnly PRAGMA + Dapper introspection shape established by
    // Migration005Fixture.
    [TestFixture]
    [Category("SqliteOnly")]
    public class Migration006Fixture : MigrationTest<v1_2_scrub_dead_language_specification_from_custom_formats>
    {
        // ============================================================
        // Test 1 — sole-entry: a CustomFormat with ONLY a LanguageSpecification
        //          spec becomes an empty Specifications array post-migration.
        // ============================================================
        [Test]
        public void should_remove_sole_language_specification_entry()
        {
            var db = WithDapperMigrationTestDb(beforeMigration: m =>
            {
                m.Execute.Sql(
                    "INSERT INTO \"CustomFormats\" (\"Name\", \"Specifications\", \"IncludeCustomFormatWhenRenaming\") " +
                    "VALUES ('LegacyEnglishOnly', " +
                    "'[{\"Type\":\"LanguageSpecification\",\"Body\":{\"Name\":\"Lang\",\"Negate\":false,\"Required\":false,\"Value\":1}}]', " +
                    "0)");
            });

            var specs = db.Query<string>(
                "SELECT \"Specifications\" FROM \"CustomFormats\" WHERE \"Name\" = 'LegacyEnglishOnly'").Single();

            specs.Should().NotContain("LanguageSpecification",
                "Migration 006 must scrub the dead spec type to prevent startup TypeLoadException");
            specs.Should().Be("[]",
                "the sole-entry shape becomes an empty array (Specifications column is NOT NULL — preserved)");
        }

        // ============================================================
        // Test 2 — multi-entry: a CustomFormat with mixed spec types keeps the
        //          surviving manga-canonical specs and only the dead one is dropped.
        // ============================================================
        [Test]
        public void should_preserve_surviving_specs_when_mixed_with_language_specification()
        {
            var db = WithDapperMigrationTestDb(beforeMigration: m =>
            {
                m.Execute.Sql(
                    "INSERT INTO \"CustomFormats\" (\"Name\", \"Specifications\", \"IncludeCustomFormatWhenRenaming\") " +
                    "VALUES ('MixedSpec', " +
                    "'[" +
                    "{\"Type\":\"ReleaseTitleSpecification\",\"Body\":{\"Name\":\"GroupX\",\"Negate\":false,\"Required\":false,\"Value\":\"GroupX\"}}," +
                    "{\"Type\":\"LanguageSpecification\",\"Body\":{\"Name\":\"Lang\",\"Negate\":false,\"Required\":false,\"Value\":1}}," +
                    "{\"Type\":\"TranslatedLanguageSpecification\",\"Body\":{\"Name\":\"TL\",\"Negate\":false,\"Required\":false,\"Value\":\"en\"}}" +
                    "]', 0)");
            });

            var specs = db.Query<string>(
                "SELECT \"Specifications\" FROM \"CustomFormats\" WHERE \"Name\" = 'MixedSpec'").Single();

            specs.Should().NotContain("\"LanguageSpecification\"",
                "the dead LanguageSpecification wrapper must be gone (anchored on both quotes so " +
                "TranslatedLanguageSpecification doesn't false-positive)");
            specs.Should().Contain("ReleaseTitleSpecification",
                "the surviving ReleaseTitleSpecification wrapper must be preserved verbatim");
            specs.Should().Contain("TranslatedLanguageSpecification",
                "the surviving manga-canonical TranslatedLanguageSpecification wrapper must be preserved verbatim");
        }

        // ============================================================
        // Test 3 — multiple LanguageSpecification entries in one array: ALL get
        //          removed (e.g., a CF that originally chained two language
        //          allowlist entries on the same spec type).
        // ============================================================
        [Test]
        public void should_remove_all_language_specification_entries_when_duplicated()
        {
            var db = WithDapperMigrationTestDb(beforeMigration: m =>
            {
                m.Execute.Sql(
                    "INSERT INTO \"CustomFormats\" (\"Name\", \"Specifications\", \"IncludeCustomFormatWhenRenaming\") " +
                    "VALUES ('TwoLangs', " +
                    "'[" +
                    "{\"Type\":\"LanguageSpecification\",\"Body\":{\"Name\":\"LangEn\",\"Negate\":false,\"Required\":false,\"Value\":1}}," +
                    "{\"Type\":\"LanguageSpecification\",\"Body\":{\"Name\":\"LangFr\",\"Negate\":false,\"Required\":false,\"Value\":2}}" +
                    "]', 0)");
            });

            var specs = db.Query<string>(
                "SELECT \"Specifications\" FROM \"CustomFormats\" WHERE \"Name\" = 'TwoLangs'").Single();

            specs.Should().NotContain("LanguageSpecification",
                "BOTH dead spec wrappers must be removed in one pass");
            specs.Should().Be("[]",
                "the entire array becomes empty when every entry is dead");
        }

        // ============================================================
        // Test 4 — idempotency: a CustomFormat with NO LanguageSpecification entry
        //          is left byte-identical (no churn for users who never had it).
        // ============================================================
        [Test]
        public void should_leave_unaffected_rows_unchanged()
        {
            var original =
                "[" +
                "{\"Type\":\"ReleaseTitleSpecification\",\"Body\":{\"Name\":\"GroupY\",\"Negate\":false,\"Required\":false,\"Value\":\"GroupY\"}}," +
                "{\"Type\":\"TranslatedLanguageSpecification\",\"Body\":{\"Name\":\"TL\",\"Negate\":false,\"Required\":false,\"Value\":\"en\"}}" +
                "]";

            var db = WithDapperMigrationTestDb(beforeMigration: m =>
            {
                m.Execute.Sql(
                    "INSERT INTO \"CustomFormats\" (\"Name\", \"Specifications\", \"IncludeCustomFormatWhenRenaming\") " +
                    "VALUES ('NoDeadSpec', '" + original.Replace("'", "''") + "', 0)");
            });

            var specs = db.Query<string>(
                "SELECT \"Specifications\" FROM \"CustomFormats\" WHERE \"Name\" = 'NoDeadSpec'").Single();

            specs.Should().Be(original,
                "rows without LanguageSpecification must NOT be rewritten — fast-filter short-circuit (idempotency)");
        }

        // ============================================================
        // Test 5 — empty table: migration is a no-op on a fresh DB without any
        //          pre-existing CustomFormats rows.
        // ============================================================
        [Test]
        public void should_be_noop_when_no_custom_formats_exist()
        {
            var db = WithDapperMigrationTestDb();

            var count = db.Query<int>("SELECT COUNT(*) FROM \"CustomFormats\"").Single();
            count.Should().Be(0, "fresh-DB users see no churn after Migration 006");
        }
    }
}
