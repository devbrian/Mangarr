using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    // Phase 31 Plan 31-01 Task 1 (TDD RED) — verifies Migration 005 deletes existing
    // AniList + MyAnimeList MetadataSourceDefinition rows from the MetadataSources table
    // per Phase 31 D-01/D-02/D-03 reframe (IL2-01). On a fresh DB with no MetadataSources
    // rows, post-migration the table is empty and InitializeProviders auto-seeds MangaDex
    // on next factory boot (verified separately via MetadataSourceFactory.InitializeProviders
    // safety net at MetadataSourceFactory.cs:92-110 — NOT exercised in this fixture).
    //
    // Mirrors the SqliteOnly PRAGMA + sqlite_master introspection shape established by
    // 001_mangarr_baselineFixture + 002_v1_1_manga_artist_demographicFixture +
    // Migration003Fixture + Migration004Fixture.
    //
    // RED → GREEN transition: Task 1 ships only the stub Migration 005 (empty MainDbUpgrade).
    // Tests in this fixture assert the DELETE behavior; they go GREEN when Task 2 fills in
    // the Execute.WithConnection DELETE SQL body.
    [TestFixture]
    [Category("SqliteOnly")]
    public class Migration005Fixture : MigrationTest<v1_2_deprecate_anilist_mal_metadata_sources>
    {
        // ============================================================
        // Test 1 — D-03: AniList row deleted from MetadataSources table
        // ============================================================
        [Test]
        public void should_delete_anilist_metadatasource_row()
        {
            var db = WithDapperMigrationTestDb(beforeMigration: m =>
            {
                m.Execute.Sql(
                    "INSERT INTO \"MetadataSources\" " +
                    "(\"Name\", \"Implementation\", \"Settings\", \"ConfigContract\", \"Enable\", \"IsPrimary\", \"Tags\") " +
                    "VALUES ('AniList', 'AniListMetadataSource', '{}', 'AniListMetadataSourceSettings', 1, 0, NULL)");
            });

            var count = db.Query<int>(
                "SELECT COUNT(*) FROM \"MetadataSources\" WHERE \"Implementation\" = 'AniListMetadataSource'").Single();
            count.Should().Be(0, "Migration 005 must delete existing AniListMetadataSource rows per D-03");
        }

        // ============================================================
        // Test 2 — D-03: MyAnimeList row deleted from MetadataSources table
        // ============================================================
        [Test]
        public void should_delete_myanimelist_metadatasource_row()
        {
            var db = WithDapperMigrationTestDb(beforeMigration: m =>
            {
                m.Execute.Sql(
                    "INSERT INTO \"MetadataSources\" " +
                    "(\"Name\", \"Implementation\", \"Settings\", \"ConfigContract\", \"Enable\", \"IsPrimary\", \"Tags\") " +
                    "VALUES ('MyAnimeList', 'MyAnimeListMetadataSource', '{}', 'MyAnimeListMetadataSourceSettings', 1, 0, NULL)");
            });

            var count = db.Query<int>(
                "SELECT COUNT(*) FROM \"MetadataSources\" WHERE \"Implementation\" = 'MyAnimeListMetadataSource'").Single();
            count.Should().Be(0, "Migration 005 must delete existing MyAnimeListMetadataSource rows per D-03");
        }

        // ============================================================
        // Test 3 — D-03: MangaDex row PRESERVED (only AniList + MAL deleted)
        // ============================================================
        [Test]
        public void should_preserve_mangadex_metadatasource_row()
        {
            var db = WithDapperMigrationTestDb(beforeMigration: m =>
            {
                m.Execute.Sql(
                    "INSERT INTO \"MetadataSources\" " +
                    "(\"Name\", \"Implementation\", \"Settings\", \"ConfigContract\", \"Enable\", \"IsPrimary\", \"Tags\") " +
                    "VALUES ('MangaDex', 'MangaDexMetadataSource', '{}', 'MangaDexMetadataSourceSettings', 1, 1, NULL)");
            });

            var count = db.Query<int>(
                "SELECT COUNT(*) FROM \"MetadataSources\" WHERE \"Implementation\" = 'MangaDexMetadataSource'").Single();
            count.Should().Be(1, "Migration 005 must NOT delete MangaDexMetadataSource rows — only AniList + MAL are deprecated per D-01");
        }

        // ============================================================
        // Test 4 — D-03 atomic cluster: seed all 3 + assert post-migration only MangaDex remains
        // ============================================================
        [Test]
        public void should_leave_only_mangadex_when_all_three_pre_seeded()
        {
            var db = WithDapperMigrationTestDb(beforeMigration: m =>
            {
                m.Execute.Sql(
                    "INSERT INTO \"MetadataSources\" " +
                    "(\"Name\", \"Implementation\", \"Settings\", \"ConfigContract\", \"Enable\", \"IsPrimary\", \"Tags\") " +
                    "VALUES " +
                    "('MangaDex', 'MangaDexMetadataSource', '{}', 'MangaDexMetadataSourceSettings', 1, 1, NULL)," +
                    "('AniList', 'AniListMetadataSource', '{}', 'AniListMetadataSourceSettings', 1, 0, NULL)," +
                    "('MyAnimeList', 'MyAnimeListMetadataSource', '{}', 'MyAnimeListMetadataSourceSettings', 1, 0, NULL)");
            });

            var implementations = db.Query<string>(
                "SELECT \"Implementation\" FROM \"MetadataSources\" ORDER BY \"Implementation\"").ToList();

            implementations.Should().BeEquivalentTo(new[] { "MangaDexMetadataSource" },
                "Migration 005 must leave exactly the MangaDexMetadataSource row when all 3 are pre-seeded (D-01 sole-primary reframe)");
        }

        // ============================================================
        // Test 5 — D-03 idempotency: re-running the DELETE on empty table is a no-op
        // ============================================================
        [Test]
        public void should_be_idempotent_when_no_anilist_or_mal_rows_present()
        {
            var db = WithDapperMigrationTestDb(beforeMigration: m =>
            {
                m.Execute.Sql(
                    "INSERT INTO \"MetadataSources\" " +
                    "(\"Name\", \"Implementation\", \"Settings\", \"ConfigContract\", \"Enable\", \"IsPrimary\", \"Tags\") " +
                    "VALUES ('MangaDex', 'MangaDexMetadataSource', '{}', 'MangaDexMetadataSourceSettings', 1, 1, NULL)");
            });

            // No AniList/MAL rows present; Migration 005 DELETE WHERE IN (...) must match zero
            // rows (idempotent — acceptable per FluentMigrator transactional semantics).
            var total = db.Query<int>("SELECT COUNT(*) FROM \"MetadataSources\"").Single();
            total.Should().Be(1, "fresh-DB users without prior AniList/MAL rows see no churn after Migration 005");
        }

        // ============================================================
        // Test 6 — IN-03 (GH #260): multiple deprecated rows of the same Implementation
        //
        // Probes DELETE WHERE IN (...) on a UNIQUE-constraint-relaxed shape: two AniList
        // rows (e.g., from a botched re-add or pre-existing user state where the unique
        // index over Implementation was absent). The DELETE must remove BOTH rows in
        // one pass; no partial-delete or skipped-row pathology.
        // ============================================================
        [Test]
        public void should_delete_multiple_deprecated_rows_of_same_implementation()
        {
            var db = WithDapperMigrationTestDb(beforeMigration: m =>
            {
                // Seed two AniList rows + one MAL row + MangaDex primary.
                // Different Name values keep both AniList rows distinguishable when the
                // unique index isn't constraining Implementation (per 001_mangarr_baseline.cs
                // schema — MetadataSources table has no UNIQUE constraint over Implementation).
                m.Execute.Sql(
                    "INSERT INTO \"MetadataSources\" " +
                    "(\"Name\", \"Implementation\", \"Settings\", \"ConfigContract\", \"Enable\", \"IsPrimary\", \"Tags\") " +
                    "VALUES " +
                    "('MangaDex', 'MangaDexMetadataSource', '{}', 'MangaDexMetadataSourceSettings', 1, 1, NULL)," +
                    "('AniList A', 'AniListMetadataSource', '{}', 'AniListMetadataSourceSettings', 1, 0, NULL)," +
                    "('AniList B', 'AniListMetadataSource', '{}', 'AniListMetadataSourceSettings', 1, 0, NULL)," +
                    "('MyAnimeList', 'MyAnimeListMetadataSource', '{}', 'MyAnimeListMetadataSourceSettings', 1, 0, NULL)");
            });

            var aniListCount = db.Query<int>(
                "SELECT COUNT(*) FROM \"MetadataSources\" WHERE \"Implementation\" = 'AniListMetadataSource'").Single();
            aniListCount.Should().Be(0, "Migration 005 DELETE WHERE IN (...) must remove ALL AniList rows in one pass, even when duplicates exist");

            var malCount = db.Query<int>(
                "SELECT COUNT(*) FROM \"MetadataSources\" WHERE \"Implementation\" = 'MyAnimeListMetadataSource'").Single();
            malCount.Should().Be(0, "Migration 005 must also delete MAL rows");

            var mangaDexCount = db.Query<int>(
                "SELECT COUNT(*) FROM \"MetadataSources\" WHERE \"Implementation\" = 'MangaDexMetadataSource'").Single();
            mangaDexCount.Should().Be(1, "MangaDex row must remain after deduplicated DELETE");
        }

        // ============================================================
        // Test 7 — WR-02 (GH #255) load-bearing: deprecated row was IsPrimary, MangaDex
        //          was IsPrimary=false → after migration MangaDex becomes IsPrimary=true.
        //
        // A user previously promoted AniList to primary via MetadataSourceFactory.SetPrimary.
        // Migration 005 deletes the AniList row (the only IsPrimary=true row). Without the
        // WR-02 defensive UPDATE, GetPrimary() would return null and downstream consumers
        // would throw InvalidOperationException or silently skip cross-source resolution.
        // This test pins the WR-02 fix landed correctly.
        // ============================================================
        [Test]
        public void should_promote_mangadex_when_deprecated_was_primary()
        {
            var db = WithDapperMigrationTestDb(beforeMigration: m =>
            {
                // Pre-Migration-005 state: AniList is primary, MangaDex exists but is not primary.
                m.Execute.Sql(
                    "INSERT INTO \"MetadataSources\" " +
                    "(\"Name\", \"Implementation\", \"Settings\", \"ConfigContract\", \"Enable\", \"IsPrimary\", \"Tags\") " +
                    "VALUES " +
                    "('MangaDex', 'MangaDexMetadataSource', '{}', 'MangaDexMetadataSourceSettings', 1, 0, NULL)," +
                    "('AniList', 'AniListMetadataSource', '{}', 'AniListMetadataSourceSettings', 1, 1, NULL)");
            });

            // AniList must be deleted (D-03).
            var aniListCount = db.Query<int>(
                "SELECT COUNT(*) FROM \"MetadataSources\" WHERE \"Implementation\" = 'AniListMetadataSource'").Single();
            aniListCount.Should().Be(0, "Migration 005 must delete AniList per D-03");

            // MangaDex must remain — and the WR-02 UPDATE must have promoted it to IsPrimary=true.
            var mangaDexIsPrimary = db.Query<int>(
                "SELECT \"IsPrimary\" FROM \"MetadataSources\" WHERE \"Implementation\" = 'MangaDexMetadataSource'").Single();
            mangaDexIsPrimary.Should().Be(1,
                "WR-02 (GH #255): when Migration 005 deletes the only IsPrimary row, MangaDex must " +
                "be defensively promoted to IsPrimary=true so GetPrimary() never returns null post-migration");

            // Exactly one IsPrimary row in the table — invariant restored.
            var primaryCount = db.Query<int>(
                "SELECT COUNT(*) FROM \"MetadataSources\" WHERE \"IsPrimary\" = 1").Single();
            primaryCount.Should().Be(1, "exactly one primary row must exist post-migration");
        }
    }
}
