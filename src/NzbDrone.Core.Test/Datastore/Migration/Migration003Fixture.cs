using System.Data.SQLite;
using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    // Phase 26 Plan 26-03 (v1.1 — INSERTED 2026-05-19) — verifies Migration 003
    // applies cleanly on a fresh DB and produces the post-cluster schema shape:
    //   - IL-01 (a) ImportLists.QualityProfileId → TranslationProfileId + CustomFormatProfileId
    //   - IL-01 (b) ImportListItems.ImdbId → MangaDexId + MalId + AniListId
    //   - IL-01 (c) ImportListExclusions.TvdbId → MangaDexId + MalId + AniListId
    //   - IL-01 (d) IX_ImportListExclusions_MangaDexId composite-unique index
    //                (NULL-tolerant per SQLite UNIQUE-with-NULLs — RESEARCH §Q4 + Q10 D6)
    //   - DP-01     DelayProfiles drops 4 cols (EnableUsenet/EnableTorrent/UsenetDelay/TorrentDelay)
    //
    // Mirrors the SqliteOnly PRAGMA + sqlite_master introspection shape established by
    // 001_mangarr_baselineFixture + 002_v1_1_manga_artist_demographicFixture.
    [TestFixture]
    [Category("SqliteOnly")]
    public class Migration003Fixture : MigrationTest<importlist_substrate_delayprofile_trim>
    {
        // ============================================================
        // IL-01 (a) — ImportLists family reshape
        // ============================================================
        [Test]
        public void ImportLists_columns_match_post_migration_shape()
        {
            var db = WithDapperMigrationTestDb();

            var cols = db.Query<TableInfoRow>("PRAGMA table_info(\"ImportLists\");")
                         .Select(r => r.name)
                         .ToList();

            cols.Should().Contain("TranslationProfileId", "TranslationProfileId must be added to ImportLists per IL-01 (a)");
            cols.Should().Contain("CustomFormatProfileId", "CustomFormatProfileId must be added to ImportLists per IL-01 (a)");
            cols.Should().NotContain("QualityProfileId", "QualityProfileId must be dropped from ImportLists per IL-01 (a) — manga uses TranslationProfile + CustomFormatProfile peers");
        }

        // ============================================================
        // IL-01 (b) — ImportListItems triplet
        // ============================================================
        [Test]
        public void ImportListItems_columns_match_post_migration_shape()
        {
            var db = WithDapperMigrationTestDb();

            var cols = db.Query<TableInfoRow>("PRAGMA table_info(\"ImportListItems\");")
                         .Select(r => r.name)
                         .ToList();

            cols.Should().Contain("MangaDexId", "MangaDexId must be added to ImportListItems per IL-01 (b)");
            cols.Should().Contain("MalId", "MalId must be added to ImportListItems per IL-01 (b)");
            cols.Should().Contain("AniListId", "AniListId must be added to ImportListItems per IL-01 (b)");
            cols.Should().NotContain("ImdbId", "ImdbId must be dropped from ImportListItems per IL-01 (b)");
            cols.Should().NotContain("TvdbId", "TvdbId was already absent pre-Migration-003 per Phase 15 D-22 (001:165)");
        }

        // ============================================================
        // IL-01 (c) — ImportListExclusions reshape
        // ============================================================
        [Test]
        public void ImportListExclusions_columns_match_post_migration_shape()
        {
            var db = WithDapperMigrationTestDb();

            var cols = db.Query<TableInfoRow>("PRAGMA table_info(\"ImportListExclusions\");")
                         .Select(r => r.name)
                         .ToList();

            cols.Should().Contain("MangaDexId", "MangaDexId must be added to ImportListExclusions per IL-01 (c)");
            cols.Should().Contain("MalId", "MalId must be added to ImportListExclusions per IL-01 (c)");
            cols.Should().Contain("AniListId", "AniListId must be added to ImportListExclusions per IL-01 (c)");
            cols.Should().NotContain("TvdbId", "TvdbId must be dropped from ImportListExclusions per IL-01 (c)");
            cols.Should().Contain("Title", "Title column survives — preserved per IL-01 (c)");
        }

        // ============================================================
        // DP-01 — DelayProfiles 4-column drop
        // ============================================================
        [Test]
        public void DelayProfiles_columns_dropped()
        {
            var db = WithDapperMigrationTestDb();

            var cols = db.Query<TableInfoRow>("PRAGMA table_info(\"DelayProfiles\");")
                         .Select(r => r.name)
                         .ToList();

            cols.Should().NotContain("EnableUsenet", "EnableUsenet must be dropped per DP-01 (Phase 23 close-out forward-schedule)");
            cols.Should().NotContain("EnableTorrent", "EnableTorrent must be dropped per DP-01");
            cols.Should().NotContain("UsenetDelay", "UsenetDelay must be dropped per DP-01");
            cols.Should().NotContain("TorrentDelay", "TorrentDelay must be dropped per DP-01");

            // Sanity: HttpDelay survives — it's the only active protocol delay post Phase 15 D-18 enum trim.
            cols.Should().Contain("HttpDelay", "HttpDelay column survives — DownloadProtocol enum trimmed to Http=3 + Unknown=0 per Phase 15 D-18");
        }

        // ============================================================
        // IL-01 (d) — NULL-tolerant unique index allows multiple NULL MangaDexId rows.
        //             RESEARCH §Q4 + Q10 D6 — AniList-only or MAL-only exclusions coexist.
        // ============================================================
        [Test]
        public void unique_index_allows_multiple_null_mangadex_ids()
        {
            var db = WithDapperMigrationTestDb();

            db.Execute(
                "INSERT INTO \"ImportListExclusions\" (\"Title\", \"MangaDexId\", \"MalId\", \"AniListId\") " +
                "VALUES (@Title, @MangaDexId, @MalId, @AniListId)",
                new { Title = "AniList-only exclusion", MangaDexId = (string)null, MalId = (int?)null, AniListId = 12345 });

            db.Execute(
                "INSERT INTO \"ImportListExclusions\" (\"Title\", \"MangaDexId\", \"MalId\", \"AniListId\") " +
                "VALUES (@Title, @MangaDexId, @MalId, @AniListId)",
                new { Title = "MAL-only exclusion", MangaDexId = (string)null, MalId = 67890, AniListId = (int?)null });

            var count = db.Query<int>("SELECT COUNT(*) FROM \"ImportListExclusions\" WHERE \"MangaDexId\" IS NULL").Single();
            count.Should().Be(2, "SQLite UNIQUE-with-NULLs must permit multiple NULL MangaDexId rows so AniList-only/MAL-only exclusions coexist (RESEARCH §Q4)");
        }

        // ============================================================
        // IL-01 (d) — positive-case: unique index rejects duplicate non-NULL MangaDexId.
        // ============================================================
        [Test]
        public void unique_index_rejects_duplicate_mangadex_id()
        {
            var db = WithDapperMigrationTestDb();

            db.Execute(
                "INSERT INTO \"ImportListExclusions\" (\"Title\", \"MangaDexId\") VALUES (@Title, @MangaDexId)",
                new { Title = "First insertion", MangaDexId = "f9c33607-9180-4663-8472-0d7c4f6df1be" });

            var act = () => db.Execute(
                "INSERT INTO \"ImportListExclusions\" (\"Title\", \"MangaDexId\") VALUES (@Title, @MangaDexId)",
                new { Title = "Duplicate insertion", MangaDexId = "f9c33607-9180-4663-8472-0d7c4f6df1be" });

            act.Should().Throw<SQLiteException>(
                "duplicate non-NULL MangaDexId must violate IX_ImportListExclusions_MangaDexId UNIQUE constraint per IL-01 (d)");
        }

        // ============================================================
        // Helper POCO mirrors `PRAGMA table_info("X")` column shape exactly.
        // ============================================================
#pragma warning disable SA1300 // Element should begin with upper-case letter
        private class TableInfoRow
        {
            public int cid { get; set; }
            public string name { get; set; }
            public string type { get; set; }
            public int notnull { get; set; }
            public string dflt_value { get; set; }
            public int pk { get; set; }
        }
#pragma warning restore SA1300
    }
}
