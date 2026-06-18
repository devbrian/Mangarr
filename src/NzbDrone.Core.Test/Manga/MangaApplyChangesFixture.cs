using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MangaTests
{
    // GH #118 — Manga.ApplyChanges contract tests, focused on the AlternativeTitles
    // merge added by parts 1+7/5 of the gh118 fix. ApplyChanges is dual-purpose
    // (metadata-source refresh AND user-PUT merge from MangaResourceMapper.ToModel);
    // the gh118 code-review followup added a defensive guard so an empty incoming
    // AlternativeTitles list does NOT clobber persisted aliases — only a
    // populated incoming list overwrites.
    [TestFixture]
    public class MangaApplyChangesFixture : CoreTest
    {
        private NzbDrone.Core.Manga.Manga _existing;

        [SetUp]
        public void Setup()
        {
            _existing = new NzbDrone.Core.Manga.Manga
            {
                Id = 1,
                Title = "Attack on Titan",
                Path = "/manga/Attack on Titan",
                AlternativeTitles = new List<string>
                {
                    "shingeki no kyojin",
                    "snk",
                    "attack on titan",
                },

                // quick-260618-eqz — seed the user-owned alt-title set so the
                // refresh-preserve / user-overwrite / empty-clear contract is testable.
                UserAlternativeTitles = new List<string>
                {
                    "my custom alias",
                    "fan title",
                },
            };
        }

        [Test]
        public void Refresh_path_overwrites_AlternativeTitles_with_populated_incoming_list()
        {
            // Metadata-source path: incoming Manga has a populated alt-title list.
            // ApplyChanges merges the new list, replacing the old one.
            var fromMetadata = new NzbDrone.Core.Manga.Manga
            {
                Title = "Attack on Titan",
                Path = "/manga/Attack on Titan",
                AlternativeTitles = new List<string>
                {
                    "shingeki no kyojin",
                    "snk",
                    "attack on titan",
                    "ataque a los titanes", // new entry from a recent refresh
                },
            };

            _existing.ApplyChanges(fromMetadata);

            _existing.AlternativeTitles.Should().BeEquivalentTo(fromMetadata.AlternativeTitles);
        }

        [Test]
        public void User_PUT_path_does_not_clobber_AlternativeTitles_when_incoming_is_empty()
        {
            // User-PUT path: MangaResourceMapper.ToModel does not populate
            // AlternativeTitles (the API resource intentionally does not expose
            // the field). The incoming Manga's AlternativeTitles is the
            // constructor-default empty list. Persisted aliases must survive.
            // AlternativeTitles defaults to new List<string>() via constructor
            var fromUserPut = new NzbDrone.Core.Manga.Manga
            {
                Title = "Attack on Titan (edited)",
                Path = "/manga/Attack on Titan",
            };

            _existing.ApplyChanges(fromUserPut);

            _existing.AlternativeTitles.Should().HaveCount(3,
                "user PUT does not surface AlternativeTitles, so the persisted set must survive an Edit-modal save");
            _existing.AlternativeTitles.Should().Contain("shingeki no kyojin");
            _existing.AlternativeTitles.Should().Contain("snk");
        }

        [Test]
        public void User_PUT_path_does_not_clobber_AlternativeTitles_when_incoming_is_null()
        {
            // Defensive: if an external caller serializes a Manga with
            // AlternativeTitles explicitly null (e.g. via a custom DTO that
            // bypasses MangaResourceMapper), the guard still protects.
            var fromExternalCaller = new NzbDrone.Core.Manga.Manga
            {
                Title = "Attack on Titan (edited)",
                Path = "/manga/Attack on Titan",
                AlternativeTitles = null,
            };

            _existing.ApplyChanges(fromExternalCaller);

            _existing.AlternativeTitles.Should().HaveCount(3);
        }

        // ─────────────────────────────────────────────────────────────────────
        // quick-260618-eqz — UserAlternativeTitles inverted-guard contract.
        // Where AlternativeTitles is metadata-OWNED (refresh overwrites, user PUT
        // preserves), UserAlternativeTitles is user-OWNED: refresh PRESERVES,
        // user PUT OVERWRITES, and an explicit empty list CLEARS. These mirror &
        // invert the AlternativeTitles tests above.
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void Refresh_path_preserves_UserAlternativeTitles_when_incoming_is_null()
        {
            // Metadata-source path: MapManga does NOT set UserAlternativeTitles, so a
            // metadata-built Manga carries null here. The inverted guard must PRESERVE
            // the stored user list — this is the core refresh-durability guarantee.
            var fromMetadata = new NzbDrone.Core.Manga.Manga
            {
                Title = "Attack on Titan",
                Path = "/manga/Attack on Titan",

                // UserAlternativeTitles intentionally left null (as MapManga leaves it).
                AlternativeTitles = new List<string> { "shingeki no kyojin" },
            };

            _existing.ApplyChanges(fromMetadata);

            _existing.UserAlternativeTitles.Should().HaveCount(2,
                "a metadata refresh (null incoming user list) must PRESERVE the user-added titles");
            _existing.UserAlternativeTitles.Should().Contain("my custom alias");
            _existing.UserAlternativeTitles.Should().Contain("fan title");
        }

        [Test]
        public void User_PUT_path_overwrites_UserAlternativeTitles_with_supplied_list()
        {
            // User-PUT path: MangaResourceMapper.ToModel produces a non-null list when the
            // request supplies the field. ApplyChanges OVERWRITES with exactly that list.
            var fromUserPut = new NzbDrone.Core.Manga.Manga
            {
                Title = "Attack on Titan",
                Path = "/manga/Attack on Titan",
                UserAlternativeTitles = new List<string> { "replacement title" },
            };

            _existing.ApplyChanges(fromUserPut);

            _existing.UserAlternativeTitles.Should().BeEquivalentTo(new[] { "replacement title" });
        }

        [Test]
        public void User_PUT_explicit_empty_list_clears_UserAlternativeTitles()
        {
            // An explicit `[]` PUT deserializes to a non-null empty list → ToModel maps a
            // non-null empty list → ApplyChanges CLEARS. Proves explicit-empty-clears (the
            // delicate half of the null-vs-empty contract).
            var fromUserPutEmpty = new NzbDrone.Core.Manga.Manga
            {
                Title = "Attack on Titan",
                Path = "/manga/Attack on Titan",
                UserAlternativeTitles = new List<string>(),
            };

            _existing.ApplyChanges(fromUserPutEmpty);

            _existing.UserAlternativeTitles.Should().NotBeNull();
            _existing.UserAlternativeTitles.Should().BeEmpty(
                "an explicit empty array must CLEAR the user-owned alt-title set");
        }

        [Test]
        public void User_and_metadata_alt_title_lists_are_independent()
        {
            // Sanity: a refresh that overwrites AlternativeTitles does NOT touch
            // UserAlternativeTitles, and supplying a user list does NOT touch
            // AlternativeTitles.
            var fromMetadataRefresh = new NzbDrone.Core.Manga.Manga
            {
                Title = "Attack on Titan",
                Path = "/manga/Attack on Titan",

                // null user list — refresh path.
                AlternativeTitles = new List<string> { "ataque a los titanes" },
            };

            _existing.ApplyChanges(fromMetadataRefresh);

            _existing.AlternativeTitles.Should().BeEquivalentTo(new[] { "ataque a los titanes" });
            _existing.UserAlternativeTitles.Should().HaveCount(2,
                "metadata refresh must not disturb the user list");

            var fromUserPut = new NzbDrone.Core.Manga.Manga
            {
                Title = "Attack on Titan",
                Path = "/manga/Attack on Titan",

                // null AlternativeTitles — user-PUT path (resource doesn't surface AlternativeTitles).
                UserAlternativeTitles = new List<string> { "only user edit" },
            };

            _existing.ApplyChanges(fromUserPut);

            _existing.UserAlternativeTitles.Should().BeEquivalentTo(new[] { "only user edit" });
            _existing.AlternativeTitles.Should().BeEquivalentTo(new[] { "ataque a los titanes" },
                "a user PUT (null AlternativeTitles) must not disturb the metadata list");
        }
    }
}
