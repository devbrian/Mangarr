using FluentAssertions;
using Mangarr.Api.V5.ImportLists;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Manga;
using NzbDrone.Test.Common;

namespace NzbDrone.Api.Test.ImportLists
{
    // #357 root-cause regression at the serialization seam.
    //
    // The bug: the frontend import-list Monitor dropdown already POSTed MangaMonitor value
    // names ("future" / "missing"), but ImportListDefinition/ImportListResource.ShouldMonitor
    // was the Sonarr-inherited MonitorTypes enum (None/All/Existing/Latest/First). The API wire
    // deserializes enums BY NAME (JsonStringEnumConverter, STJson.ApplySerializerSettings), so
    // {"shouldMonitor":"future"} hit a JsonException -> HTTP 400 — the save failed.
    //
    // The fix retyped ShouldMonitor to the canonical 7-value MangaMonitor (which gained
    // Existing + First, so nothing is dropped). This fixture proves, using the SAME serializer
    // the API uses, that all 7 names now round-trip by-name WITHOUT throwing — the fast,
    // server-free proof of the #357 fix.
    [TestFixture]
    public class ImportListResourceDeserializationFixture : TestBase
    {
        [Test]
        public void deserializes_future_shouldMonitor_without_throwing()
        {
            // The exact case the frontend hit: {"shouldMonitor":"future"} used to 400.
            var resource = STJson.Deserialize<ImportListResource>("{\"shouldMonitor\":\"future\"}");

            resource.ShouldMonitor.Should().Be(MangaMonitor.Future,
                "#357: 'future' must deserialize by-name to MangaMonitor.Future (was a JsonException -> 400)");
        }

        [Test]
        public void deserializes_missing_shouldMonitor_without_throwing()
        {
            var resource = STJson.Deserialize<ImportListResource>("{\"shouldMonitor\":\"missing\"}");

            resource.ShouldMonitor.Should().Be(MangaMonitor.Missing,
                "#357: 'missing' must deserialize by-name to MangaMonitor.Missing (was a JsonException -> 400)");
        }

        [TestCase("all", MangaMonitor.All)]
        [TestCase("future", MangaMonitor.Future)]
        [TestCase("missing", MangaMonitor.Missing)]
        [TestCase("existing", MangaMonitor.Existing)]
        [TestCase("first", MangaMonitor.First)]
        [TestCase("latest", MangaMonitor.Latest)]
        [TestCase("none", MangaMonitor.None)]
        public void all_seven_monitor_names_round_trip_by_name(string name, MangaMonitor expected)
        {
            // by-name (de)serialization is ordinal-agnostic, so every one of the 7 legitimate
            // MangaMonitor values is accepted at the wire — the #357 fix WIDENS the accepted set
            // to exactly these 7 without loosening validation (an undefined name still throws).
            var json = $"{{\"shouldMonitor\":\"{name}\"}}";

            var resource = STJson.Deserialize<ImportListResource>(json);

            resource.ShouldMonitor.Should().Be(expected);
        }
    }
}
