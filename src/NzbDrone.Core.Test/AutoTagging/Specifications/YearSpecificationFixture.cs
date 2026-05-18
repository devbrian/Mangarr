using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.AutoTagging.Specifications;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.AutoTagging.Specifications
{
    // Phase 24 v1.1 Wave 3 — YearSpecification per-spec coverage.
    // PublicationYear is int? on Manga (vs Sonarr's non-nullable Year); nullable
    // guard returns false for unset publication year per PATTERNS lines 713-715.
    [TestFixture]
    public class YearSpecificationFixture : CoreTest
    {
        [Test]
        public void Is_satisfied_when_year_within_range()
        {
            var manga = new NzbDrone.Core.Manga.Manga { PublicationYear = 2020 };
            var spec = new YearSpecification { Min = 2018, Max = 2022 };

            spec.IsSatisfiedBy(manga).Should().BeTrue();
        }

        [Test]
        public void Is_not_satisfied_when_year_below_min()
        {
            var manga = new NzbDrone.Core.Manga.Manga { PublicationYear = 2017 };
            var spec = new YearSpecification { Min = 2018, Max = 2022 };

            spec.IsSatisfiedBy(manga).Should().BeFalse();
        }

        [Test]
        public void Is_not_satisfied_when_year_above_max()
        {
            var manga = new NzbDrone.Core.Manga.Manga { PublicationYear = 2023 };
            var spec = new YearSpecification { Min = 2018, Max = 2022 };

            spec.IsSatisfiedBy(manga).Should().BeFalse();
        }

        [Test]
        public void Is_not_satisfied_when_publication_year_is_null()
        {
            // Nullable guard — PublicationYear is int? on Manga, Sonarr's Year is int.
            var manga = new NzbDrone.Core.Manga.Manga { PublicationYear = null };
            var spec = new YearSpecification { Min = 2018, Max = 2022 };

            spec.IsSatisfiedBy(manga).Should().BeFalse();
        }

        [Test]
        public void Order_is_one_per_sonarr_canonical()
        {
            new YearSpecification().Order.Should().Be(1);
        }

        [Test]
        public void ImplementationName_matches_catalog_row()
        {
            new YearSpecification().ImplementationName.Should().Be("Year");
        }

        [Test]
        public void Validate_fails_when_Min_is_zero()
        {
            var spec = new YearSpecification { Min = 0, Max = 2022 };

            var result = spec.Validate();

            result.IsValid.Should().BeFalse();
        }
    }
}
