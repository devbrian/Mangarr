using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.AutoTagging.Specifications;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.AutoTagging
{
    // Phase 24 v1.1 Wave-0 — verifies SpecificationMatchesGroup.DidMatch semantic
    // PRESERVED VERBATIM from the Sonarr 6f857ba0e^ restore baseline (Pitfall 3
    // anti-rewrite gate). Asserts the OR-within-type / AND-with-Required-override
    // contract + the literal source-text guard against a naive future rewrite
    // (e.g. Matches.All(m => m.Value)).
    [TestFixture]
    public class SpecificationMatchesGroupFixture : TestBase
    {
        private static Mock<IAutoTaggingSpecification> BuildSpec(bool required)
        {
            var spec = new Mock<IAutoTaggingSpecification>();
            spec.SetupGet(s => s.Required).Returns(required);
            return spec;
        }

        [Test]
        public void empty_matches_returns_false_due_to_vacuous_all_predicate()
        {
            // Empty Matches dictionary → both Any() and All() vacuously evaluate the
            // boolean — Any() returns false (no element satisfies the predicate) and
            // All() returns true (vacuous truth). So !(false || true) = !(true) = false.
            // Sonarr-canonical: empty group → DidMatch = false (no specs to match).
            // This pins the upstream contract — empty group behaves like all-false.
            var group = new SpecificationMatchesGroup
            {
                Matches = new Dictionary<IAutoTaggingSpecification, bool>(),
            };

            group.DidMatch.Should().BeFalse(
                "an empty Matches dictionary fails the !All(m => m.Value == false) branch (vacuous true) — Sonarr-canonical behavior preserved");
        }

        [Test]
        public void all_matches_false_returns_false()
        {
            var specA = BuildSpec(required: false);
            var specB = BuildSpec(required: false);

            var group = new SpecificationMatchesGroup
            {
                Matches = new Dictionary<IAutoTaggingSpecification, bool>
                {
                    { specA.Object, false },
                    { specB.Object, false },
                },
            };

            group.DidMatch.Should().BeFalse("all-false group must not match (Sonarr-canonical OR-within-type)");
        }

        [Test]
        public void all_matches_true_returns_true()
        {
            var specA = BuildSpec(required: false);
            var specB = BuildSpec(required: false);

            var group = new SpecificationMatchesGroup
            {
                Matches = new Dictionary<IAutoTaggingSpecification, bool>
                {
                    { specA.Object, true },
                    { specB.Object, true },
                },
            };

            group.DidMatch.Should().BeTrue("all-true group matches (Sonarr-canonical)");
        }

        [Test]
        public void mixed_matches_without_required_returns_true()
        {
            var specA = BuildSpec(required: false);
            var specB = BuildSpec(required: false);

            var group = new SpecificationMatchesGroup
            {
                Matches = new Dictionary<IAutoTaggingSpecification, bool>
                {
                    { specA.Object, true },
                    { specB.Object, false },
                },
            };

            group.DidMatch.Should().BeTrue("OR-within-type: one true is enough when no Required override (Sonarr-canonical)");
        }

        [Test]
        public void mixed_matches_with_required_on_failing_spec_returns_false()
        {
            // Required override forces AND semantic — even though specA matched,
            // specB.Required=true with specB matched=false makes the group fail.
            var specA = BuildSpec(required: false);
            var specB = BuildSpec(required: true);

            var group = new SpecificationMatchesGroup
            {
                Matches = new Dictionary<IAutoTaggingSpecification, bool>
                {
                    { specA.Object, true },
                    { specB.Object, false },
                },
            };

            group.DidMatch.Should().BeFalse("Required override on a failing spec must force the whole group to fail (Sonarr-canonical AND-with-Required)");
        }

        // ========================================================================
        // Pitfall 3 anti-rewrite gate — the actual SpecificationMatchesGroup.cs
        // source file MUST contain the verbatim Sonarr DidMatch expression. This
        // test reads the file and asserts the literal expression text is present,
        // so a naive future "cleanup" rewrite (e.g. Matches.All(m => m.Value))
        // is caught even if the unit-behavior tests above would still pass for
        // some inputs.
        // ========================================================================
        [Test]
        public void source_file_contains_verbatim_didmatch_expression()
        {
            var sourcePath = FindSourceFile("AutoTagging", "SpecificationMatchesGroup.cs");
            File.Exists(sourcePath).Should().BeTrue($"SpecificationMatchesGroup.cs must exist at {sourcePath}");

            // The Sonarr 6f857ba0e^ baseline wraps the DidMatch expression across two lines
            // at the `||` (line-broken between Required-override clause and All-false clause).
            // Normalize whitespace (collapse all runs of any whitespace to a single space)
            // before asserting — that absorbs the upstream line-wrap without weakening the
            // anti-rewrite contract (Pitfall 3).
            var source = File.ReadAllText(sourcePath);
            var normalized = System.Text.RegularExpressions.Regex.Replace(source, @"\s+", " ");

            normalized.Should().Contain(
                "!(Matches.Any(m => m.Key.Required && m.Value == false) || Matches.All(m => m.Value == false))",
                "DidMatch expression must match the Sonarr 6f857ba0e^ baseline byte-for-byte (whitespace-normalized) — Pitfall 3 anti-rewrite gate (Phase 24 v1.1). DO NOT rewrite as `Matches.All(m => m.Value)` — the OR-within-type / Required-override semantic depends on the literal expression shape.");
        }

        private static string FindSourceFile(string folder, string fileName)
        {
            // Walk up from the test bin output until we find src/NzbDrone.Core/<folder>/<fileName>.
            // Mirrors how other Mangarr fixtures locate live source files (e.g. for
            // attribute introspection tests).
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "src", "NzbDrone.Core", folder, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            return Path.Combine("src", "NzbDrone.Core", folder, fileName);
        }
    }
}
