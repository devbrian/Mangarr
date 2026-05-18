using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.AutoTagging.Specifications;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.AutoTagging.Specifications
{
    // Phase 24 v1.1 Wave 3 — MonitoredSpecification per-spec coverage.
    // Sonarr-canonical has NO Value field; the rule fires on monitored=true.
    [TestFixture]
    public class MonitoredSpecificationFixture : CoreTest
    {
        [Test]
        public void Is_satisfied_when_manga_is_monitored()
        {
            var manga = new NzbDrone.Core.Manga.Manga { Monitored = true };
            var spec = new MonitoredSpecification();

            spec.IsSatisfiedBy(manga).Should().BeTrue();
        }

        [Test]
        public void Is_not_satisfied_when_manga_is_unmonitored()
        {
            var manga = new NzbDrone.Core.Manga.Manga { Monitored = false };
            var spec = new MonitoredSpecification();

            spec.IsSatisfiedBy(manga).Should().BeFalse();
        }

        [Test]
        public void Order_is_one_per_sonarr_canonical()
        {
            new MonitoredSpecification().Order.Should().Be(1);
        }

        [Test]
        public void ImplementationName_matches_catalog_row()
        {
            new MonitoredSpecification().ImplementationName.Should().Be("Monitored");
        }

        [Test]
        public void Validate_succeeds_with_default_state()
        {
            // MonitoredSpecificationValidator has no rules per Sonarr canonical.
            var spec = new MonitoredSpecification();

            spec.Validate().IsValid.Should().BeTrue();
        }
    }
}
