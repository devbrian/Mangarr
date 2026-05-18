using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.AutoTagging.Specifications;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.AutoTagging.Specifications
{
    // Phase 24 v1.1 Wave 3 — RootFolderSpecification per-spec coverage.
    // Field name unchanged (Manga.RootFolderPath); uses PathEquals extension which
    // is platform-aware (case-insensitive on Windows, case-sensitive on POSIX).
    [TestFixture]
    public class RootFolderSpecificationFixture : CoreTest
    {
        [Test]
        public void Is_satisfied_when_root_folder_matches()
        {
            var manga = new NzbDrone.Core.Manga.Manga { RootFolderPath = "/manga" };
            var spec = new RootFolderSpecification { Value = "/manga" };

            spec.IsSatisfiedBy(manga).Should().BeTrue();
        }

        [Test]
        public void Is_not_satisfied_when_root_folder_differs()
        {
            var manga = new NzbDrone.Core.Manga.Manga { RootFolderPath = "/manga" };
            var spec = new RootFolderSpecification { Value = "/other" };

            spec.IsSatisfiedBy(manga).Should().BeFalse();
        }

        [Test]
        public void Order_is_one_per_sonarr_canonical()
        {
            new RootFolderSpecification().Order.Should().Be(1);
        }

        [Test]
        public void ImplementationName_matches_catalog_row()
        {
            new RootFolderSpecification().ImplementationName.Should().Be("Root Folder");
        }

        [Test]
        public void Validate_fails_when_Value_is_empty()
        {
            var spec = new RootFolderSpecification { Value = string.Empty };

            spec.Validate().IsValid.Should().BeFalse();
        }
    }
}
