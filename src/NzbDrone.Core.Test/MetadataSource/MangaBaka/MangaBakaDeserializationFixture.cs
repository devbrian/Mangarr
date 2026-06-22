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

            // quick-260608-l2e — the five additional cross-source ids. kitsu/ann/shikimori are
            // ints; anime_planet/manga_updates are STRINGS. This is the regression guard that the
            // string-vs-int ref typing matches the live wire shape (a future int-typing of the
            // string refs would throw a JsonReaderException right here, not silently at runtime).
            resource.Data.Source.Kitsu.Id.Should().Be(54114);
            resource.Data.Source.AnimeNewsNetwork.Id.Should().Be(25998);
            resource.Data.Source.Shikimori.Id.Should().Be(121496);
            resource.Data.Source.AnimePlanet.Id.Should().Be("solo-leveling");
            resource.Data.Source.MangaUpdates.Id.Should().Be("6z1uqw7");
        }

        // Phase 42 (D-12 / DISC-03): the series record must round-trip the `state` field (so the
        // merged/deleted skip is not a silent no-op) and the `rating` int. A future drop of either
        // [JsonProperty] fails HERE (T-42-01-DTO).
        [Test]
        public void series_record_round_trips_state_and_rating()
        {
            const string json = @"{ ""id"": 42, ""title"": ""Test Manga"", ""state"": ""merged"", ""rating"": 78 }";

            var series = Common.Serializer.Json.Deserialize<MangaBakaSeries>(json);

            series.Should().NotBeNull();
            series.State.Should().Be("merged");
            series.Rating.Should().Be(78);
        }

        // PITFALL 2 analog regression: the live `rating` is a FRACTIONAL number, not an integer.
        // A future `int?` retyping would throw a JsonReaderException right here (fails the whole
        // response) rather than silently at runtime.
        [Test]
        public void series_record_round_trips_fractional_rating()
        {
            const string json = @"{ ""id"": 42, ""title"": ""Test Manga"", ""rating"": 86.2083333333333 }";

            var series = Common.Serializer.Json.Deserialize<MangaBakaSeries>(json);

            series.Should().NotBeNull();
            series.Rating.Should().Be(86.2083333333333m);
        }

        // DISC-03: the slim genre option list round-trips { label, value } from { data[] }.
        [Test]
        public void genre_list_envelope_round_trips_slim_options()
        {
            const string json = @"{ ""data"": [ { ""label"": ""Boys' Love"", ""value"": ""boys_love"" }, { ""label"": ""Action"", ""value"": ""action"" } ] }";

            var resource = Common.Serializer.Json.Deserialize<MangaBakaGenreListResource>(json);

            resource.Should().NotBeNull();
            resource.Data.Should().HaveCount(2);
            resource.Data.Should().Contain(g => g.Label == "Boys' Love" && g.Value == "boys_love");
        }

        // DISC-03 / D-10: the slim tag DTO binds the integer id + name_path + series_count and
        // DROPS the long blurb — even when the wire payload carries it, the slim DTO ignores it
        // (Newtonsoft A1) and exposes only the modelled fields.
        [Test]
        public void tag_list_envelope_drops_description_and_keeps_slim_fields()
        {
            const string json = @"{ ""data"": [ { ""id"": 363, ""name"": ""Isekai"", ""name_path"": ""Genre > Isekai"", ""series_count"": 1234, ""content_rating"": ""safe"", ""description"": ""A long unused blurb"", ""is_spoiler"": false, ""parent_id"": 12, ""level"": 2 } ] }";

            var resource = Common.Serializer.Json.Deserialize<MangaBakaTagListResource>(json);

            resource.Should().NotBeNull();
            resource.Data.Should().HaveCount(1);

            var tag = resource.Data[0];
            tag.Id.Should().Be(363);
            tag.Name.Should().Be("Isekai");
            tag.NamePath.Should().Be("Genre > Isekai");
            tag.SeriesCount.Should().Be(1234);
            tag.ContentRating.Should().Be("safe");

            // The slim DTO has no description member — the wire `description` key is silently
            // dropped, and the only public string properties are the modelled slim ones.
            typeof(MangaBakaTag).GetProperty("Description").Should().BeNull(
                "the slim tag DTO deliberately omits description (DISC-03)");
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
