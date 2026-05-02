using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.MangaDex;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.MangaDex
{
    /// <summary>
    /// Wave 0 stub fixture for <see cref="MangaDexParser"/>. References NOT-YET-BUILT production
    /// type (lands in Plan 03-04). Loads the live-captured MangaDex /manga/{id}/feed JSON from
    /// <c>Files/Indexers/MangaDex/feed_*.json</c> (committed in Plan 03-01 Task 1).
    ///
    /// Coverage:
    /// - SOURCE-04: ScanlationGroup field extraction from relationships[type=scanlation_group]
    /// - SOURCE-04: TranslatedLanguage BCP-47 extraction
    /// - D-12: decimal-aware ChapterNumber parsing (e.g. "281.1", "261.5")
    /// - DownloadUrl shape: at-home/server endpoint pattern
    /// - DownloadProtocol.Http for Mangarr aggregator sources (vs Sonarr's Usenet/Torrent)
    /// </summary>
    [TestFixture]
    public class MangaDexParserFixture : CoreTest<MangaDexParser>
    {
        private string _onePieceFeed;
        private string _kaguyaFeed;

        [SetUp]
        public void Setup()
        {
            // feed_one_piece.json: 6 EN entries (rest delisted upstream by Shueisha licensing)
            _onePieceFeed = File.ReadAllText("Files/Indexers/MangaDex/feed_one_piece.json");

            // feed_kaguya.json: substitute for canonical Solo Leveling — provides decimal coverage
            // (281.1, 261.5, 251.5, 241.5...) required for D-12 decimal-aware ChapterNumber assertions.
            _kaguyaFeed = File.ReadAllText("Files/Indexers/MangaDex/feed_kaguya.json");
        }

        [Test]
        public void ParseResponse_extracts_scanlation_group()
        {
            var response = MakeResponse(_onePieceFeed);
            var releases = Subject.ParseResponse(response);
            releases.Any(r => !string.IsNullOrEmpty(r.ScanlationGroup)).Should().BeTrue();
        }

        [Test]
        public void ParseResponse_extracts_translated_language_BCP47()
        {
            var response = MakeResponse(_onePieceFeed);
            var releases = Subject.ParseResponse(response);
            releases.Any(r => r.TranslatedLanguage == "en").Should().BeTrue();
        }

        [Test]
        public void ParseResponse_handles_decimal_chapter_numbers()
        {
            // Kaguya feed has decimal chapters: 281.1, 261.5, 251.5, 241.5...
            var response = MakeResponse(_kaguyaFeed);
            var releases = Subject.ParseResponse(response);
            releases.Any(r => Regex.IsMatch(r.Title ?? string.Empty, @"\d+\.\d+")).Should().BeTrue();
        }

        [Test]
        public void ParseResponse_sets_DownloadUrl_to_at_home_server()
        {
            var response = MakeResponse(_onePieceFeed);
            var releases = Subject.ParseResponse(response);
            releases.First().DownloadUrl.Should().StartWith("https://api.mangadex.org/at-home/server/");
        }

        [Test]
        public void ParseResponse_sets_DownloadProtocol_Http()
        {
            var response = MakeResponse(_onePieceFeed);
            var releases = Subject.ParseResponse(response);
            releases.First().DownloadProtocol.Should().Be(DownloadProtocol.Http);
        }

        private IndexerResponse MakeResponse(string content)
        {
            // Note: IndexerRequest has (string, HttpAccept) and (HttpRequest) ctors but no
            // (HttpRequest, HttpAccept) overload — Plan 03-01 scaffold typed this incorrectly.
            // Pre-existing bug surfaced when Plan 03-04 wired production types; repaired inline
            // (Rule 1 — bug) so MangaDex Wave 0 fixtures can compile and exercise the parser.
            var req = new HttpRequest("https://api.mangadex.org/manga/x/feed");
            var resp = new HttpResponse(req, new HttpHeader(), content);
            return new IndexerResponse(new IndexerRequest(req), resp);
        }
    }
}
