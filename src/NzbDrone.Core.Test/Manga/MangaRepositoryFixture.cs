using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Test.Framework;
using MangaModel = NzbDrone.Core.Manga.Manga;
using MangaRepository = NzbDrone.Core.Manga.MangaRepository;

namespace NzbDrone.Core.Test.MangaTests
{
    // Wave 1 live fixture for IMangaRepository (CRUD + finders by external IDs).
    // Plan 02-03 lands MangaRepository / IMangaRepository.
    [TestFixture]
    public class MangaRepositoryFixture : DbTest<MangaRepository, MangaModel>
    {
        private MangaModel BuildManga(string title = "Naruto", Guid? mangaDexId = null, int? malId = null, int? aniListId = null)
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
            };
        }

        [Test]
        public void Insert_returns_id()
        {
            var manga = BuildManga();
            Subject.Insert(manga);
            manga.Id.Should().BeGreaterThan(0);
        }

        // Issue #270 (Migration 008): Manga.Added (NOT NULL) + Manga.LastInfoSync
        // (nullable) are UTC-instant columns swept to timestamptz on Postgres. Round-trip
        // must be bit-for-bit on both backends.
        [Test]
        public void Insert_persists_Added_and_LastInfoSync_utc_round_trip()
        {
            var added = new DateTime(2026, 2, 1, 6, 15, 0, DateTimeKind.Utc);
            var synced = new DateTime(2026, 2, 2, 18, 45, 0, DateTimeKind.Utc);
            var manga = BuildManga();
            manga.Added = added;
            manga.LastInfoSync = synced;
            Subject.Insert(manga);

            var fetched = Subject.Get(manga.Id);
            fetched.Added.Should().Be(added);
            fetched.Added.Kind.Should().Be(DateTimeKind.Utc);
            fetched.LastInfoSync.Should().Be(synced);
            fetched.LastInfoSync.Value.Kind.Should().Be(DateTimeKind.Utc);
        }

        [Test]
        public void Get_by_id_returns_inserted()
        {
            var manga = BuildManga();
            Subject.Insert(manga);

            var fetched = Subject.Get(manga.Id);

            fetched.Should().NotBeNull();
            fetched.Title.Should().Be("Naruto");
            fetched.CleanTitle.Should().Be("naruto");
        }

        [Test]
        public void FindByMangaDexId_returns_match()
        {
            var mangaDexId = Guid.NewGuid();
            Subject.Insert(BuildManga("Naruto", mangaDexId: mangaDexId));
            Subject.Insert(BuildManga("Bleach", mangaDexId: Guid.NewGuid()));

            var found = Subject.FindByMangaDexId(mangaDexId);

            found.Should().NotBeNull();
            found.Title.Should().Be("Naruto");
        }

        [Test]
        public void FindByMalId_returns_match()
        {
            Subject.Insert(BuildManga("Naruto", malId: 11));
            Subject.Insert(BuildManga("Bleach", malId: 22));

            var found = Subject.FindByMalId(22);

            found.Should().NotBeNull();
            found.Title.Should().Be("Bleach");
        }

        [Test]
        public void FindByAniListId_returns_match()
        {
            Subject.Insert(BuildManga("Naruto", aniListId: 111));
            Subject.Insert(BuildManga("Bleach", aniListId: 222));

            var found = Subject.FindByAniListId(111);

            found.Should().NotBeNull();
            found.Title.Should().Be("Naruto");
        }

        [Test]
        public void Update_persists_changes()
        {
            var manga = BuildManga();
            Subject.Insert(manga);

            manga.Status = MangaStatusType.Completed;
            manga.Overview = "Story of a ninja";
            Subject.Update(manga);

            var fetched = Subject.Get(manga.Id);
            fetched.Status.Should().Be("completed");
            fetched.Overview.Should().Be("Story of a ninja");
        }

        [Test]
        public void Delete_removes_row()
        {
            var manga = BuildManga();
            Subject.Insert(manga);

            Subject.Delete(manga.Id);

            Subject.All().Should().BeEmpty();
        }

        [Test]
        public void All_returns_all()
        {
            Subject.Insert(BuildManga("Naruto"));
            Subject.Insert(BuildManga("Bleach"));

            Subject.All().ToList().Should().HaveCount(2);
        }

        // ─────────────────────────────────────────────────────────────────────
        // GH #118 — FindByAlternativeTitle coverage (Strategy 2 of the
        // multi-strategy GetManga). Ambiguity safety added per code-review
        // followup: return null on multi-candidate matches so the caller falls
        // through to FindByTitleInexact (matching the Sonarr-canonical posture
        // and the existing FindByTitleInexact contract).
        // ─────────────────────────────────────────────────────────────────────

        private MangaModel BuildWithAltTitles(string title, params string[] altTitles)
        {
            var manga = BuildManga(title);
            manga.AlternativeTitles = new List<string>(altTitles);
            return manga;
        }

        [Test]
        public void FindByAlternativeTitle_returns_single_match()
        {
            Subject.Insert(BuildWithAltTitles("Attack on Titan", "shingeki no kyojin", "snk"));
            Subject.Insert(BuildWithAltTitles("Bleach", "burichi"));

            var found = Subject.FindByAlternativeTitle("shingeki no kyojin");

            found.Should().NotBeNull();
            found.Title.Should().Be("Attack on Titan");
        }

        [Test]
        public void FindByAlternativeTitle_returns_null_on_no_match()
        {
            Subject.Insert(BuildWithAltTitles("Attack on Titan", "shingeki no kyojin"));

            Subject.FindByAlternativeTitle("naruto").Should().BeNull();
        }

        [Test]
        public void FindByAlternativeTitle_returns_null_when_no_alt_titles_populated()
        {
            // Manga added before metadata refresh has empty AlternativeTitles.
            Subject.Insert(BuildManga("Naruto"));

            Subject.FindByAlternativeTitle("naruto").Should().BeNull();
        }

        [Test]
        public void FindByAlternativeTitle_returns_null_when_normalized_input_empty()
        {
            Subject.Insert(BuildWithAltTitles("Attack on Titan", "shingeki no kyojin"));

            Subject.FindByAlternativeTitle(null).Should().BeNull();
            Subject.FindByAlternativeTitle("").Should().BeNull();
            Subject.FindByAlternativeTitle("   ").Should().BeNull();
        }

        [Test]
        public void FindByAlternativeTitle_does_not_match_substring_overlap_between_distinct_entries()
        {
            // Defense against substring-match false positives — the entry is
            // stored as JSON-quoted "snk", and a search for "sn" must not match.
            // The repo wraps the lookup pattern in JSON-element quotes to ensure
            // an exact entry-level match, not a substring within an entry.
            Subject.Insert(BuildWithAltTitles("Attack on Titan", "snk"));

            Subject.FindByAlternativeTitle("sn").Should().BeNull();
        }

        [Test]
        public void FindByAlternativeTitle_returns_null_on_ambiguous_multi_candidate_match()
        {
            // Two manga can legitimately share an alt-title — e.g. a doujinshi
            // and its parent series both list a romanized synonym. Return null
            // rather than nondeterministically picking one; the caller
            // (MangaParsingService.GetManga Strategy 2 -> Strategy 3) treats
            // null as "no match" and falls through to FindByTitleInexact.
            Subject.Insert(BuildWithAltTitles("Attack on Titan", "shingeki no kyojin"));
            Subject.Insert(BuildWithAltTitles("Attack on Titan Doujinshi", "shingeki no kyojin"));

            Subject.FindByAlternativeTitle("shingeki no kyojin").Should().BeNull(
                "ambiguous alt-title resolution must return null so the caller falls through to Strategy 3 (FindByTitleInexact) — Sonarr-canonical posture");
        }

        // ─────────────────────────────────────────────────────────────────────
        // quick-260618-eqz — FindByAlternativeTitle now also searches the
        // user-owned UserAlternativeTitles column. A match in EITHER list resolves
        // the manga; the single-match-only / ambiguous-returns-null guard holds
        // across the broadened query.
        // ─────────────────────────────────────────────────────────────────────

        private MangaModel BuildWithUserAltTitles(string title, params string[] userAltTitles)
        {
            var manga = BuildManga(title);
            manga.UserAlternativeTitles = new List<string>(userAltTitles);
            return manga;
        }

        [Test]
        public void FindByAlternativeTitle_resolves_via_user_alt_titles_only()
        {
            // The match key lives ONLY in UserAlternativeTitles (pre-normalized, as the
            // API mapper stores it) — no metadata source supplied it. The broadened query
            // must still resolve the manga.
            Subject.Insert(BuildWithUserAltTitles("Attack on Titan", "shingeki no kyojin user"));
            Subject.Insert(BuildManga("Bleach"));

            var found = Subject.FindByAlternativeTitle("shingeki no kyojin user");

            found.Should().NotBeNull();
            found.Title.Should().Be("Attack on Titan");
        }

        [Test]
        public void FindByAlternativeTitle_returns_null_on_ambiguous_user_alt_title_match()
        {
            // Two manga sharing the same user-added alias must return null (single-match-only
            // semantics hold across the broadened user-column query).
            Subject.Insert(BuildWithUserAltTitles("Attack on Titan", "shared user alias"));
            Subject.Insert(BuildWithUserAltTitles("Attack on Titan Doujinshi", "shared user alias"));

            Subject.FindByAlternativeTitle("shared user alias").Should().BeNull(
                "ambiguous user-alt-title resolution must return null — single-match-only semantics hold across both lists");
        }
    }
}
