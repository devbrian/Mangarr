using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.AutoTagging.Specifications;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.AutoTagging.Specifications
{
    // Phase 24 v1.1 Wave 3 — DemographicSpecification per-spec coverage (D-04).
    // Manga.Demographic is nullable enum; null -> no match.
    [TestFixture]
    public class DemographicSpecificationFixture : CoreTest
    {
        [Test]
        public void Is_satisfied_when_demographic_matches()
        {
            var manga = new NzbDrone.Core.Manga.Manga { Demographic = MangaDemographic.Shonen };
            var spec = new DemographicSpecification { Value = (int)MangaDemographic.Shonen };

            spec.IsSatisfiedBy(manga).Should().BeTrue();
        }

        [Test]
        public void Is_not_satisfied_when_demographic_differs()
        {
            var manga = new NzbDrone.Core.Manga.Manga { Demographic = MangaDemographic.Shojo };
            var spec = new DemographicSpecification { Value = (int)MangaDemographic.Shonen };

            spec.IsSatisfiedBy(manga).Should().BeFalse();
        }

        [Test]
        public void Is_not_satisfied_when_demographic_is_null()
        {
            var manga = new NzbDrone.Core.Manga.Manga { Demographic = null };
            var spec = new DemographicSpecification { Value = (int)MangaDemographic.Shonen };

            spec.IsSatisfiedBy(manga).Should().BeFalse();
        }

        [Test]
        public void Order_is_one_per_sonarr_canonical()
        {
            new DemographicSpecification().Order.Should().Be(1);
        }

        [Test]
        public void ImplementationName_matches_catalog_row()
        {
            new DemographicSpecification().ImplementationName.Should().Be("Demographic");
        }

        [Test]
        public void Validate_fails_when_Value_is_zero()
        {
            var spec = new DemographicSpecification { Value = 0 };

            spec.Validate().IsValid.Should().BeFalse();
        }
    }
}
