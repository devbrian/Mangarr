using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.AutoTagging.Specifications;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.AutoTagging.Specifications
{
    // Phase 24 v1.1 Wave 3 — StatusSpecification per-spec coverage WITH Pitfall 1 reshape.
    // Manga.Status is STRING (NOT a Sonarr-style SeriesStatus enum). Anti-cast gate W-1:
    // this fixture MUST NOT cast through the Sonarr SeriesStatus enum -- only MangaStatus
    // casts are permitted. Status strings come from MangaStatusType static-class constants
    // (Ongoing="ongoing", Completed="completed", Hiatus="hiatus", Cancelled="cancelled").
    [TestFixture]
    public class StatusSpecificationFixture : CoreTest
    {
        [Test]
        public void Is_satisfied_when_status_matches_enum_value_case_insensitive()
        {
            var manga = new NzbDrone.Core.Manga.Manga { Status = MangaStatusType.Ongoing };
            var spec = new StatusSpecification { Value = (int)MangaStatus.Ongoing };

            spec.IsSatisfiedBy(manga).Should().BeTrue();
        }

        [Test]
        public void Is_satisfied_for_completed_status()
        {
            var manga = new NzbDrone.Core.Manga.Manga { Status = MangaStatusType.Completed };
            var spec = new StatusSpecification { Value = (int)MangaStatus.Completed };

            spec.IsSatisfiedBy(manga).Should().BeTrue();
        }

        [Test]
        public void Is_not_satisfied_when_status_differs()
        {
            var manga = new NzbDrone.Core.Manga.Manga { Status = MangaStatusType.Ongoing };
            var spec = new StatusSpecification { Value = (int)MangaStatus.Completed };

            spec.IsSatisfiedBy(manga).Should().BeFalse();
        }

        [Test]
        public void Is_not_satisfied_when_status_is_null()
        {
            var manga = new NzbDrone.Core.Manga.Manga { Status = null };
            var spec = new StatusSpecification { Value = (int)MangaStatus.Ongoing };

            spec.IsSatisfiedBy(manga).Should().BeFalse();
        }

        [Test]
        public void Is_not_satisfied_when_status_is_empty_string()
        {
            var manga = new NzbDrone.Core.Manga.Manga { Status = string.Empty };
            var spec = new StatusSpecification { Value = (int)MangaStatus.Ongoing };

            spec.IsSatisfiedBy(manga).Should().BeFalse();
        }

        [Test]
        public void Order_is_one_per_sonarr_canonical()
        {
            new StatusSpecification().Order.Should().Be(1);
        }

        [Test]
        public void ImplementationName_matches_catalog_row()
        {
            new StatusSpecification().ImplementationName.Should().Be("Status");
        }

        [Test]
        public void Validate_succeeds_with_any_int_value()
        {
            // StatusSpecificationValidator has no rules per Sonarr canonical.
            var spec = new StatusSpecification { Value = (int)MangaStatus.Hiatus };

            spec.Validate().IsValid.Should().BeTrue();
        }
    }
}
