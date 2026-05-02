using System.IO;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Comix;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.Comix
{
    /// <summary>
    /// Wave 0 stub fixture for <see cref="ComixParser"/>. References NOT-YET-BUILT production
    /// type (lands in Plan 03-05). Loads <c>Files/Indexers/Comix/manga_chapters_*.json</c>
    /// (committed in Plan 03-01 Task 1).
    ///
    /// Coverage:
    /// - SOURCE-04: Comix scanlation_group extraction (when present) — Comix surfaces
    ///   group via the <c>scanlation_group</c> object on each Chapter row
    /// - SOURCE-04: TranslatedLanguage is hard-coded "en" (single-language-source per RESEARCH);
    ///   Comix DOES NOT carry a language code on chapter rows
    /// - DownloadProtocol.Http for Comix
    /// </summary>
    [TestFixture]
    public class ComixParserFixture : CoreTest<ComixParser>
    {
        private string _onePieceChapters;

        [SetUp]
        public void Setup()
        {
            _onePieceChapters = File.ReadAllText("Files/Indexers/Comix/manga_chapters_one_piece.json");
        }

        [Test]
        public void ParseResponse_extracts_scanlation_group_when_present()
        {
            var response = MakeResponse(_onePieceChapters);
            var releases = Subject.ParseResponse(response);
            releases.Any(r => !string.IsNullOrEmpty(r.ScanlationGroup)).Should().BeTrue();
        }

        [Test]
        public void ParseResponse_sets_TranslatedLanguage_to_en()
        {
            // RESEARCH: Comix is single-source English-only; parser hard-codes "en".
            var response = MakeResponse(_onePieceChapters);
            var releases = Subject.ParseResponse(response);
            releases.Should().OnlyContain(r => r.TranslatedLanguage == "en");
        }

        [Test]
        public void ParseResponse_handles_decimal_chapter_numbers()
        {
            // Synthesized fixture has 1099.5 / 1096.5 / 1091.5 etc.
            var response = MakeResponse(_onePieceChapters);
            var releases = Subject.ParseResponse(response);
            releases.Any(r => System.Text.RegularExpressions.Regex.IsMatch(
                r.Title ?? string.Empty, @"\d+\.\d+")).Should().BeTrue();
        }

        [Test]
        public void ParseResponse_sets_DownloadProtocol_Http()
        {
            var response = MakeResponse(_onePieceChapters);
            var releases = Subject.ParseResponse(response);
            releases.First().DownloadProtocol.Should().Be(DownloadProtocol.Http);
        }

        private IndexerResponse MakeResponse(string content)
        {
            // Note: IndexerRequest has (string, HttpAccept) and (HttpRequest) ctors but no
            // (HttpRequest, HttpAccept) overload — Plan 03-01 scaffold typed this incorrectly.
            // Pre-existing bug surfaced when Plan 03-05 wired production types; repaired inline
            // (Rule 1 — bug) so Comix Wave 0 fixtures can compile and exercise the parser.
            // Mirrors the identical repair Plan 03-04 made to MangaDexParserFixture.cs.
            var req = new HttpRequest("https://comix.to/api/v2/manga/x/chapters");
            var resp = new HttpResponse(req, new HttpHeader(), content);
            return new IndexerResponse(new IndexerRequest(req), resp);
        }
    }
}
