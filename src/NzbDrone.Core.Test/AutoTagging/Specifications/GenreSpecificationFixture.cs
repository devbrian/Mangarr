using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.AutoTagging.Specifications;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.AutoTagging.Specifications
{
    // Phase 24 v1.1 Wave 3 — per-spec coverage for GenreSpecification.
    // Verifies IsSatisfiedBy contract (case-insensitive Contains match) + Order/
    // ImplementationName constants + Validate() rejects empty Value.
    [TestFixture]
    public class GenreSpecificationFixture : CoreTest
    {
        private NzbDrone.Core.Manga.Manga _manga;

        [SetUp]
        public void Setup()
        {
            _manga = new NzbDrone.Core.Manga.Manga
            {
                Id = 1,
                Title = "Test Manga",
                Genres = new List<string> { "Action", "Adventure" }
            };
        }

        [Test]
        public void Is_satisfied_when_genre_matches_case_insensitively()
        {
            var spec = new GenreSpecification { Value = new[] { "adventure" } };

            spec.IsSatisfiedBy(_manga).Should().BeTrue();
        }

        [Test]
        public void Is_not_satisfied_when_no_genre_matches()
        {
            _manga.Genres = new List<string> { "Action" };
            var spec = new GenreSpecification { Value = new[] { "Adventure" } };

            spec.IsSatisfiedBy(_manga).Should().BeFalse();
        }

        [Test]
        public void Order_is_one_per_sonarr_canonical()
        {
            new GenreSpecification().Order.Should().Be(1);
        }

        [Test]
        public void ImplementationName_matches_catalog_row()
        {
            new GenreSpecification().ImplementationName.Should().Be("Genre");
        }

        [Test]
        public void Validate_fails_when_Value_is_empty()
        {
            var spec = new GenreSpecification { Value = new List<string>() };

            var result = spec.Validate();

            result.IsValid.Should().BeFalse();
        }
    }
}
