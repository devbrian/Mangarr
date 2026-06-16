using System;
using System.Data.SQLite;
using FluentAssertions;
using Npgsql;
using NUnit.Framework;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Test.Framework;
using MangaModel = NzbDrone.Core.Manga.Manga;
using MangaRepository = NzbDrone.Core.Manga.MangaRepository;

namespace NzbDrone.Core.Test.MangaTests
{
    // Issue #213 regression: Manga.MangaDexId / MalId / AniListId carry UNIQUE
    // indexes (Migration 001 IX_Manga_{MangaDexId,MalId,AniListId}) that close the
    // concurrent-POST race in AddMangaService.PrepareForAdd. Pre-fix, two near-
    // simultaneous POSTs with the same external ID both passed the in-memory
    // FindBy*Id existence check before either insert committed, producing duplicate
    // Manga rows pointing at the same canonical metadata.
    //
    // The fix is two-layered: this fixture exercises the DB-layer constraint
    // directly (asserting that the second Insert surfaces the dialect-specific
    // constraint violation). The MangaController.AddManga catch-blocks that map
    // the constraint violation to HTTP 409 Conflict are verified by the
    // integration / live-verify path in the PR body.
    //
    // SQLite + PostgreSQL both treat NULL as distinct per ANSI SQL — multiple
    // null-Id rows must remain legal because every Manga only needs ONE of the
    // three IDs populated (MangaController.PostValidator at MangaController.cs:72-74).
    //
    // The DbTest framework switches between SQLite (default) and PostgreSQL
    // based on PostgresOptions env-driven host detection; this fixture covers
    // BOTH dialects via a single assertion helper that maps the column-axis
    // hint to either SQLiteException.Message or PostgresException.ConstraintName.
    [TestFixture]
    public class MangaRepositoryUniqueConstraintFixture : DbTest<MangaRepository, MangaModel>
    {
        private MangaModel BuildManga(string title, Guid? mangaDexId = null, int? malId = null, int? aniListId = null, int? mangaBakaId = null)
        {
            return new MangaModel
            {
                Title = title,
                CleanTitle = title.ToLowerInvariant(),
                SortTitle = title.ToLowerInvariant(),
                Status = MangaStatusType.Ongoing,
                Path = $"C:\\manga\\{title}",
                Monitored = true,
                Added = DateTime.UtcNow,
                MangaDexId = mangaDexId,
                MalId = malId,
                AniListId = aniListId,
                MangaBakaId = mangaBakaId,
            };
        }

        // Provider-agnostic assertion: SQLite throws SQLiteException with
        // ResultCode == Constraint and a message of form
        // "UNIQUE constraint failed: Manga.<Column>"; PostgreSQL throws
        // PostgresException with SqlState == "23505" and ConstraintName like
        // "IX_Manga_<Column>". Match on the column hint that's common to both.
        private static bool IsUniqueConstraintViolation(Exception ex, string columnHint)
        {
            if (ex is SQLiteException sqliteEx)
            {
                return sqliteEx.ResultCode == SQLiteErrorCode.Constraint &&
                       sqliteEx.Message.Contains(columnHint, StringComparison.Ordinal);
            }

            if (ex is PostgresException pgEx)
            {
                return pgEx.SqlState == "23505" &&
                       (pgEx.ConstraintName?.Contains(columnHint, StringComparison.Ordinal) ?? false);
            }

            return false;
        }

        [Test]
        public void Insert_with_duplicate_MangaDexId_throws_constraint_violation()
        {
            var mangaDexId = Guid.NewGuid();
            Subject.Insert(BuildManga("Naruto", mangaDexId: mangaDexId));

            Action act = () => Subject.Insert(BuildManga("Naruto-dup", mangaDexId: mangaDexId));

            act.Should().Throw<Exception>()
                .Where(ex => IsUniqueConstraintViolation(ex, "MangaDexId"),
                    "the UNIQUE index IX_Manga_MangaDexId must reject the second insert (SQLite or PostgreSQL)");
        }

        [Test]
        public void Insert_with_duplicate_MalId_throws_constraint_violation()
        {
            Subject.Insert(BuildManga("Naruto", malId: 11));

            Action act = () => Subject.Insert(BuildManga("Naruto-dup", malId: 11));

            act.Should().Throw<Exception>()
                .Where(ex => IsUniqueConstraintViolation(ex, "MalId"),
                    "the UNIQUE index IX_Manga_MalId must reject the second insert (SQLite or PostgreSQL)");
        }

        [Test]
        public void Insert_with_duplicate_AniListId_throws_constraint_violation()
        {
            Subject.Insert(BuildManga("Naruto", aniListId: 101));

            Action act = () => Subject.Insert(BuildManga("Naruto-dup", aniListId: 101));

            act.Should().Throw<Exception>()
                .Where(ex => IsUniqueConstraintViolation(ex, "AniListId"),
                    "the UNIQUE index IX_Manga_AniListId must reject the second insert (SQLite or PostgreSQL)");
        }

        [Test]
        public void Insert_with_duplicate_MangaBakaId_throws_constraint_violation()
        {
            // Migration 014 — IX_Manga_MangaBakaId UNIQUE closes the concurrent-POST race for
            // the v1.3 default-primary's standalone anchor ID (parity with the big-3 above).
            Subject.Insert(BuildManga("Naruto", mangaBakaId: 5050));

            Action act = () => Subject.Insert(BuildManga("Naruto-dup", mangaBakaId: 5050));

            act.Should().Throw<Exception>()
                .Where(ex => IsUniqueConstraintViolation(ex, "MangaBakaId"),
                    "the UNIQUE index IX_Manga_MangaBakaId must reject the second insert (SQLite or PostgreSQL)");
        }

        [Test]
        public void Insert_two_manga_with_null_MangaBakaId_succeeds()
        {
            Subject.Insert(BuildManga("MangaDex-only-1", mangaDexId: Guid.NewGuid()));
            Subject.Insert(BuildManga("MangaDex-only-2", mangaDexId: Guid.NewGuid()));

            Subject.All().Should().HaveCount(2);
        }

        [Test]
        public void Insert_two_manga_with_null_MangaDexId_succeeds()
        {
            // ANSI SQL NULL-distinct semantic: many manga can legitimately lack a
            // MangaDexId (e.g. AniList-only or MAL-only metadata source) and they
            // must coexist. The UNIQUE index does NOT collide on NULL.
            Subject.Insert(BuildManga("AniList-only-1", aniListId: 1));
            Subject.Insert(BuildManga("AniList-only-2", aniListId: 2));

            Subject.All().Should().HaveCount(2);
        }

        [Test]
        public void Insert_two_manga_with_null_MalId_succeeds()
        {
            Subject.Insert(BuildManga("MangaDex-only-1", mangaDexId: Guid.NewGuid()));
            Subject.Insert(BuildManga("MangaDex-only-2", mangaDexId: Guid.NewGuid()));

            Subject.All().Should().HaveCount(2);
        }

        [Test]
        public void Insert_two_manga_with_null_AniListId_succeeds()
        {
            Subject.Insert(BuildManga("Mal-only-1", malId: 10));
            Subject.Insert(BuildManga("Mal-only-2", malId: 20));

            Subject.All().Should().HaveCount(2);
        }

        [Test]
        public void Insert_distinct_external_ids_succeed_for_same_external_source()
        {
            // Sanity check — distinct IDs on the same source axis must both insert.
            Subject.Insert(BuildManga("Naruto", mangaDexId: Guid.NewGuid()));
            Subject.Insert(BuildManga("Bleach", mangaDexId: Guid.NewGuid()));

            Subject.All().Should().HaveCount(2);
        }
    }
}
