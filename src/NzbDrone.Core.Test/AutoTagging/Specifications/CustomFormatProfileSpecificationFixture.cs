using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.AutoTagging.Specifications;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.AutoTagging.Specifications
{
    // Phase 24 v1.1 Wave 3 — CustomFormatProfileSpecification per-spec coverage.
    // Other half of the AT-03 QualityProfile split-add. Compares
    // Manga.CustomFormatProfileId (nullable int) to the rule's Value.
    [TestFixture]
    public class CustomFormatProfileSpecificationFixture : CoreTest
    {
        [Test]
        public void Is_satisfied_when_profile_id_matches()
        {
            var manga = new NzbDrone.Core.Manga.Manga { CustomFormatProfileId = 3 };
            var spec = new CustomFormatProfileSpecification { Value = 3 };

            spec.IsSatisfiedBy(manga).Should().BeTrue();
        }

        [Test]
        public void Is_not_satisfied_when_profile_id_differs()
        {
            var manga = new NzbDrone.Core.Manga.Manga { CustomFormatProfileId = 3 };
            var spec = new CustomFormatProfileSpecification { Value = 2 };

            spec.IsSatisfiedBy(manga).Should().BeFalse();
        }

        [Test]
        public void Is_not_satisfied_when_profile_id_is_null()
        {
            var manga = new NzbDrone.Core.Manga.Manga { CustomFormatProfileId = null };
            var spec = new CustomFormatProfileSpecification { Value = 1 };

            spec.IsSatisfiedBy(manga).Should().BeFalse();
        }

        [Test]
        public void Order_is_one_per_sonarr_canonical()
        {
            new CustomFormatProfileSpecification().Order.Should().Be(1);
        }

        [Test]
        public void ImplementationName_matches_catalog_row()
        {
            new CustomFormatProfileSpecification().ImplementationName.Should().Be("Custom Format Profile");
        }

        [Test]
        public void Validate_fails_when_Value_is_zero()
        {
            var spec = new CustomFormatProfileSpecification { Value = 0 };

            spec.Validate().IsValid.Should().BeFalse();
        }
    }
}
