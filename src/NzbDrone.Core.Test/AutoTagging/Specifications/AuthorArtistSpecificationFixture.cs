using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.AutoTagging.Specifications;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.AutoTagging.Specifications
{
    // Phase 24 v1.1 Wave 3 — AuthorArtistSpecification per-spec coverage (D-03 combined OR).
    // Matches PrimaryAuthor OR Artist case-insensitively; both-null safely returns false.
    [TestFixture]
    public class AuthorArtistSpecificationFixture : CoreTest
    {
        [Test]
        public void Is_satisfied_when_primary_author_matches_case_insensitively()
        {
            var manga = new NzbDrone.Core.Manga.Manga
            {
                PrimaryAuthor = "Solo Leveling Author",
                Artist = null
            };
            var spec = new AuthorArtistSpecification { Value = new[] { "solo leveling author" } };

            spec.IsSatisfiedBy(manga).Should().BeTrue();
        }

        [Test]
        public void Is_satisfied_when_artist_matches_case_insensitively()
        {
            var manga = new NzbDrone.Core.Manga.Manga
            {
                PrimaryAuthor = null,
                Artist = "Chugong"
            };
            var spec = new AuthorArtistSpecification { Value = new[] { "chugong" } };

            spec.IsSatisfiedBy(manga).Should().BeTrue();
        }

        [Test]
        public void Is_not_satisfied_when_neither_matches()
        {
            var manga = new NzbDrone.Core.Manga.Manga
            {
                PrimaryAuthor = "Foo",
                Artist = "Bar"
            };
            var spec = new AuthorArtistSpecification { Value = new[] { "baz" } };

            spec.IsSatisfiedBy(manga).Should().BeFalse();
        }

        [Test]
        public void Is_not_satisfied_when_both_author_and_artist_null()
        {
            var manga = new NzbDrone.Core.Manga.Manga
            {
                PrimaryAuthor = null,
                Artist = null
            };
            var spec = new AuthorArtistSpecification { Value = new[] { "anyone" } };

            spec.IsSatisfiedBy(manga).Should().BeFalse();
        }

        [Test]
        public void Order_is_one_per_sonarr_canonical()
        {
            new AuthorArtistSpecification().Order.Should().Be(1);
        }

        [Test]
        public void ImplementationName_matches_catalog_row()
        {
            new AuthorArtistSpecification().ImplementationName.Should().Be("Author / Artist");
        }

        [Test]
        public void Validate_fails_when_Value_is_empty()
        {
            var spec = new AuthorArtistSpecification { Value = new List<string>() };

            spec.Validate().IsValid.Should().BeFalse();
        }
    }
}
