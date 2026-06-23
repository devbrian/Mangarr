#nullable enable
using System.Collections.Generic;
using FluentAssertions;
using Mangarr.Api.V5.Manga;
using NUnit.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Api.Test.Manga
{
    // quick-260623-imh regression guard. The metadata-sourced Manga.AlternativeTitles set is
    // surfaced READ-ONLY on the wire: ToResource emits it (GET feeds the details-page alt-title
    // hover Popover) but ToModel deliberately does NOT round-trip it (refresh-owned — a crafted
    // PUT must not overwrite the stored set; T-imh-01 mass-assignment mitigation). These tests
    // lock that inverted-ownership contract so a future regression can't let a PUT clobber the
    // metadata-owned set.
    [TestFixture]
    public class MangaResourceMapperFixture : TestBase
    {
        [Test]
        public void ToResource_carries_AlternativeTitles_in_order()
        {
            var model = new Core.Manga.Manga
            {
                AlternativeTitles = new List<string> { "ローマ字版", "Romanized Title" }
            };

            var resource = model.ToResource();

            resource!.AlternativeTitles.Should().Equal("ローマ字版", "Romanized Title");
        }

        [Test]
        public void ToResource_maps_empty_AlternativeTitles_to_empty_non_null_list()
        {
            var model = new Core.Manga.Manga
            {
                AlternativeTitles = new List<string>()
            };

            var resource = model.ToResource();

            resource!.AlternativeTitles.Should().NotBeNull();
            resource.AlternativeTitles.Should().BeEmpty();
        }

        [Test]
        public void ToModel_does_NOT_round_trip_AlternativeTitles()
        {
            var resource = new MangaResource
            {
                AlternativeTitles = new List<string> { "Injected A", "Injected B" }
            };

            var model = resource.ToModel();

            // ToModel intentionally omits the AlternativeTitles assignment, so the resulting
            // Manga carries the constructor default EMPTY list — the injected titles are dropped.
            model!.AlternativeTitles.Should().NotBeNull();
            model.AlternativeTitles.Should().BeEmpty();
        }
    }
}
