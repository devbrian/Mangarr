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
    }
}
