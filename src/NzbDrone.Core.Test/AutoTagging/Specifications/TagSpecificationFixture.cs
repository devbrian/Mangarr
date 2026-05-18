using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.AutoTagging.Specifications;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.AutoTagging.Specifications
{
    // Phase 24 v1.1 Wave 3 — TagSpecification per-spec coverage.
    // Manga.Tags is HashSet<int> (same shape as Sonarr.Series.Tags); spec matches
    // when the rule's Value tag id is present in the manga's tag set.
    [TestFixture]
    public class TagSpecificationFixture : CoreTest
    {
        [Test]
        public void Is_satisfied_when_tag_present_in_manga_tags()
        {
            var manga = new NzbDrone.Core.Manga.Manga { Tags = new HashSet<int> { 1, 2 } };
            var spec = new TagSpecification { Value = 1 };

            spec.IsSatisfiedBy(manga).Should().BeTrue();
        }

        [Test]
        public void Is_not_satisfied_when_tag_absent()
        {
            var manga = new NzbDrone.Core.Manga.Manga { Tags = new HashSet<int> { 2 } };
            var spec = new TagSpecification { Value = 1 };

            spec.IsSatisfiedBy(manga).Should().BeFalse();
        }

        [Test]
        public void Is_not_satisfied_when_manga_has_no_tags()
        {
            var manga = new NzbDrone.Core.Manga.Manga { Tags = new HashSet<int>() };
            var spec = new TagSpecification { Value = 1 };

            spec.IsSatisfiedBy(manga).Should().BeFalse();
        }

        [Test]
        public void Order_is_one_per_sonarr_canonical()
        {
            new TagSpecification().Order.Should().Be(1);
        }

        [Test]
        public void ImplementationName_matches_catalog_row()
        {
            new TagSpecification().ImplementationName.Should().Be("Tag");
        }

        [Test]
        public void Validate_fails_when_Value_is_zero()
        {
            var spec = new TagSpecification { Value = 0 };

            spec.Validate().IsValid.Should().BeFalse();
        }
    }
}
