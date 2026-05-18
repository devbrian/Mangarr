using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.AutoTagging.Specifications;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Test.AutoTagging
{
    // Phase 24 v1.1 Wave 3 — verifies the NEGATE flag inversion semantic inherited
    // by every concrete IAutoTaggingSpecification implementation. The base class
    // wraps IsSatisfiedByWithoutNegate with an inversion guard when Negate=true.
    // Asserting once at the base level removes the need to duplicate the negate
    // assertion across all 11 per-spec fixtures.
    [TestFixture]
    public class AutoTaggingSpecificationBaseFixture : CoreTest
    {
        // Minimal concrete subclass that lets the test drive IsSatisfiedByWithoutNegate
        // via a delegate so we can flip true/false without inventing a Manga field.
        private sealed class StubSpec : AutoTaggingSpecificationBase
        {
            public bool Match { get; set; }

            public override int Order => 1;
            public override string ImplementationName => "Stub";

            protected override bool IsSatisfiedByWithoutNegate(NzbDrone.Core.Manga.Manga manga) => Match;

            public override NzbDroneValidationResult Validate() => new();
        }

        [Test]
        public void IsSatisfiedBy_returns_match_when_negate_is_false()
        {
            var manga = new NzbDrone.Core.Manga.Manga();
            var spec = new StubSpec { Match = true, Negate = false };

            spec.IsSatisfiedBy(manga).Should().BeTrue();
        }

        [Test]
        public void IsSatisfiedBy_returns_not_match_when_negate_is_false()
        {
            var manga = new NzbDrone.Core.Manga.Manga();
            var spec = new StubSpec { Match = false, Negate = false };

            spec.IsSatisfiedBy(manga).Should().BeFalse();
        }

        [Test]
        public void IsSatisfiedBy_inverts_match_when_negate_is_true()
        {
            var manga = new NzbDrone.Core.Manga.Manga();
            var spec = new StubSpec { Match = true, Negate = true };

            spec.IsSatisfiedBy(manga).Should().BeFalse(
                "Negate=true must invert the IsSatisfiedByWithoutNegate result");
        }

        [Test]
        public void IsSatisfiedBy_inverts_non_match_when_negate_is_true()
        {
            var manga = new NzbDrone.Core.Manga.Manga();
            var spec = new StubSpec { Match = false, Negate = true };

            spec.IsSatisfiedBy(manga).Should().BeTrue(
                "Negate=true must invert the IsSatisfiedByWithoutNegate result");
        }

        [Test]
        public void Clone_returns_distinct_instance_with_same_field_values()
        {
            var spec = new StubSpec { Match = true, Negate = true, Name = "Original", Required = true };

            var clone = spec.Clone();

            clone.Should().NotBeSameAs(spec);
            clone.Should().BeOfType<StubSpec>();
            clone.Name.Should().Be("Original");
            clone.Negate.Should().BeTrue();
            clone.Required.Should().BeTrue();
        }
    }
}
