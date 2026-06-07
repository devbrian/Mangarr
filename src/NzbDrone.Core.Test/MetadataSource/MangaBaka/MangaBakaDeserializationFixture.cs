using System.IO;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource.MangaBaka.Resource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.MangaBaka
{
    /// <summary>
    /// Regression guard for the live MangaBaka wire shape (Phase 41 close-out).
    ///
    /// <para>
    /// <see cref="MangaBakaMetadataSourceFixture"/> mocks <c>IHttpClient.Get&lt;T&gt;</c> with
    /// inline Resource POCOs, so it NEVER exercises JSON -> DTO deserialization — which is
    /// exactly where a real defect lived: <c>secondary_titles</c> was typed
    /// <c>Dictionary&lt;string,string&gt;</c>, but the API ships
    /// <c>Dictionary&lt;string, List&lt;{ type, title, note }&gt;&gt;</c>. Newtonsoft threw a
    /// <c>JsonReaderException</c> ("Unexpected character '['") that failed the ENTIRE response,
    /// so live search 500'd and by-id silently returned empty — undetectable by the POCO-mock
    /// fixture. These tests deserialize REAL captured payloads
    /// (<c>Files/MetadataSource/MangaBaka/*.json</c>, captured live from
    /// <c>api.mangabaka.org</c>) through the project Newtonsoft pipeline so any future
    /// wire-shape regression fails HERE rather than silently at runtime.
    /// </para>
    /// </summary>
    [TestFixture]
    public class MangaBakaDeserializationFixture : CoreTest
    {
        private static string Load(string name)
            => File.ReadAllText(Path.Combine("Files", "MetadataSource", "MangaBaka", name));

        [Test]
        public void by_id_envelope_deserializes_real_payload()
        {
            var resource = Common.Serializer.Json.Deserialize<MangaBakaSeriesResource>(
                Load("series_by_id_3397.json"));

            resource.Should().NotBeNull();
            resource.Data.Should().NotBeNull("the {status,data} by-id envelope wraps a single record");
            resource.Data.Id.Should().Be(3397);
            resource.Data.Title.Should().Be("Solo Leveling");

            // The bug class: secondary_titles is a language-keyed dict whose VALUES are
            // ARRAYS of localized-title objects — NOT a flat string map.
            resource.Data.SecondaryTitles.Should().NotBeNull();
            resource.Data.SecondaryTitles.Values
                .SelectMany(v => v)
                .Select(e => e.Title)
                .Should().Contain(t => !string.IsNullOrWhiteSpace(t),
                    "secondary_titles[lang][].title must parse into MangaBakaSecondaryTitle");

            // total_chapters is a STRING (Pitfall 2); the direct cross-source ids are ints (D-08-R).
            resource.Data.TotalChapters.Should().NotBeNullOrWhiteSpace();
            resource.Data.Source.Should().NotBeNull();
            resource.Data.Source.AniList.Id.Should().Be(105398);
            resource.Data.Source.MyAnimeList.Id.Should().Be(121496);
        }

        [Test]
        public void search_envelope_deserializes_real_payload()
        {
            var resource = Common.Serializer.Json.Deserialize<MangaBakaSearchResource>(
                Load("search_solo_leveling.json"));

            resource.Should().NotBeNull();
            resource.Data.Should().NotBeNullOrEmpty(
                "the {status,pagination,data[]} search envelope wraps a record array");
            resource.Data.Should().Contain(s => s.Title == "Solo Leveling");

            // Every record's secondary_titles must deserialize (the path that previously threw
            // and failed the whole search response).
            resource.Data
                .Where(s => s.SecondaryTitles != null)
                .SelectMany(s => s.SecondaryTitles.Values)
                .SelectMany(list => list)
                .Should().OnlyContain(e => e != null,
                    "every nested secondary-title entry must parse without a JsonReaderException");
        }
    }
}
