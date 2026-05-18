using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.AutoTagging.Specifications;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.AutoTagging.Specifications
{
    // Phase 24 v1.1 Wave 3 — ContentRatingSpecification per-spec coverage.
    // MangaContentRating enum (Safe/Suggestive/Erotica/Pornographic) backs the
    // FE dropdown; Manga.ContentRating itself stays as string on the entity.
    // Evaluation maps int Value -> enum -> ToString() -> case-insensitive equality
    // against the stored string.
    [TestFixture]
    public class ContentRatingSpecificationFixture : CoreTest
    {
        [Test]
        public void Is_satisfied_when_content_rating_matches_case_insensitively()
        {
            var manga = new NzbDrone.Core.Manga.Manga { ContentRating = "safe" };
            var spec = new ContentRatingSpecification { Value = (int)MangaContentRating.Safe };

            spec.IsSatisfiedBy(manga).Should().BeTrue();
        }

        [Test]
        public void Is_satisfied_when_content_rating_mixed_case_matches()
        {
            var manga = new NzbDrone.Core.Manga.Manga { ContentRating = "Suggestive" };
            var spec = new ContentRatingSpecification { Value = (int)MangaContentRating.Suggestive };

            spec.IsSatisfiedBy(manga).Should().BeTrue();
        }

        [Test]
        public void Is_not_satisfied_when_content_rating_differs()
        {
            var manga = new NzbDrone.Core.Manga.Manga { ContentRating = "safe" };
            var spec = new ContentRatingSpecification { Value = (int)MangaContentRating.Pornographic };

            spec.IsSatisfiedBy(manga).Should().BeFalse();
        }

        [Test]
        public void Is_not_satisfied_when_content_rating_is_null()
        {
            var manga = new NzbDrone.Core.Manga.Manga { ContentRating = null };
            var spec = new ContentRatingSpecification { Value = (int)MangaContentRating.Safe };

            spec.IsSatisfiedBy(manga).Should().BeFalse();
        }

        [Test]
        public void Is_not_satisfied_when_content_rating_is_empty_string()
        {
            var manga = new NzbDrone.Core.Manga.Manga { ContentRating = string.Empty };
            var spec = new ContentRatingSpecification { Value = (int)MangaContentRating.Safe };

            spec.IsSatisfiedBy(manga).Should().BeFalse();
        }

        [Test]
        public void Order_is_one_per_sonarr_canonical()
        {
            new ContentRatingSpecification().Order.Should().Be(1);
        }

        [Test]
        public void ImplementationName_matches_catalog_row()
        {
            new ContentRatingSpecification().ImplementationName.Should().Be("Content Rating");
        }

        [Test]
        public void Validate_fails_when_Value_is_zero()
        {
            var spec = new ContentRatingSpecification { Value = 0 };

            spec.Validate().IsValid.Should().BeFalse();
        }
    }
}
