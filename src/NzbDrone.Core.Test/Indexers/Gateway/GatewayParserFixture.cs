using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Gateway;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.Gateway
{
    /// <summary>
    /// Highest-value Wave-0 fixture for <see cref="GatewayParser"/> (lands in Plan 37-03 Task 2 —
    /// this references the NOT-YET-BUILT production type and compiles GREEN only after Task 2;
    /// intentional contract-first ordering per the Wave-0 convention).
    ///
    /// Coverage (the 6 VALIDATION.md load-bearing locks):
    /// - reconstructs_title_when_hints_complete (D-02): a complete-hint release reconstructs a
    ///   title that ROUND-TRIPS <see cref="MangaParser.ParseChapterTitle"/> to the right manga +
    ///   chapter (A1 — NOT a string-equals).
    /// - title_only_release_survives_verbatim (D-02b): an all-null-hints release surfaces VERBATIM
    ///   and is NEVER dropped (the UI-visible InteractiveSearch win).
    /// - decimal_chapter_title_precision (D-02 / Pitfall 3): 179.0 → 179 (no trailing .0), 12.5,
    ///   1.123 — each round-trips to the exact decimal.
    /// - maps_release_fields_and_dedupes_by_guid (GWIX-03): downloadHandle → DownloadUrl,
    ///   language → TranslatedLanguage, scanlationGroup preserved, PublishDate set,
    ///   DownloadProtocol.Http. The parser does NOT itself de-dup (Plan 04's Fetch via
    ///   CleanupReleases collapses the duplicate-guid row).
    /// - warning_records_failure_and_returns_releases (GWIX-04 / D-03a): each warnings[] entry →
    ///   RecordFailure(sourceKey) AND the good releases are still returned.
    /// - warning_never_throws (GWIX-04): ParseResponse never throws on a warning.
    /// </summary>
    [TestFixture]
    public class GatewayParserFixture : CoreTest<GatewayParser>
    {
        private string _searchJson;
        private string _recentJson;

        [SetUp]
        public void Setup()
        {
            _searchJson = File.ReadAllText("Files/Indexers/Gateway/search.json");
            _recentJson = File.ReadAllText("Files/Indexers/Gateway/recent.json");
        }

        [Test]
        public void reconstructs_title_when_hints_complete()
        {
            var releases = Subject.ParseResponse(MakeResponse(_searchJson));

            // The complete-hint row (mangaTitle "Solo Leveling", chapterNumber 179.0).
            var release = releases.Single(r => r.Guid == "comix.to:solo-leveling:179"
                                               && r.DownloadUrl.Contains("MTc5In0"));

            // A1 round-trip: the reconstructed Title MUST parse back to the right manga + chapter.
            var parsed = MangaParser.ParseChapterTitle(release.Title);
            parsed.Should().NotBeNull();
            parsed.MangaTitle.Should().Be("Solo Leveling");
            parsed.ChapterNumbers.Should().Contain(179m);
            parsed.ScanlationGroup.Should().Be("Team Lumikha");
            parsed.TranslatedLanguage.Should().Be("en");
        }

        [Test]
        public void title_only_release_survives_verbatim()
        {
            var releases = Subject.ParseResponse(MakeResponse(_searchJson));

            // The title-only row: mangaTitle == null && chapterNumber == null → NEVER dropped.
            var release = releases.SingleOrDefault(r => r.Guid == "comix.to:raw:v3-ch5");

            release.Should().NotBeNull("a title-only release must never be dropped (D-02b)");
            release.Title.Should().Be("Some Raw Release v3 ch 5", "the verbatim gateway title is preserved");
        }

        [Test]
        public void decimal_chapter_title_precision()
        {
            var releases = Subject.ParseResponse(MakeResponse(_searchJson));

            var c179 = releases.Single(r => r.Guid == "comix.to:solo-leveling:179"
                                            && r.DownloadUrl.Contains("MTc5In0"));
            var c125 = releases.Single(r => r.Guid == "comix.to:solo-leveling:12.5");
            var c1123 = releases.Single(r => r.Guid == "comix.to:solo-leveling:1.123");

            // 179.0 → no trailing .0.
            c179.Title.Should().Contain("179");
            c179.Title.Should().NotContain("179.0");

            // 12.5 + 1.123 → no precision loss.
            c125.Title.Should().Contain("12.5");
            c1123.Title.Should().Contain("1.123");

            // Each round-trips to the exact decimal.
            MangaParser.ParseChapterTitle(c179.Title).ChapterNumbers.Should().Contain(179m);
            MangaParser.ParseChapterTitle(c125.Title).ChapterNumbers.Should().Contain(12.5m);
            MangaParser.ParseChapterTitle(c1123.Title).ChapterNumbers.Should().Contain(1.123m);
        }

        [Test]
        public void maps_release_fields_and_dedupes_by_guid()
        {
            var releases = Subject.ParseResponse(MakeResponse(_searchJson));

            var release = releases.First(r => r.Guid == "comix.to:solo-leveling:179"
                                              && r.DownloadUrl.Contains("MTc5In0"));

            // downloadHandle → DownloadUrl (opaque R6 token, faithfully mapped).
            release.DownloadUrl.Should().Be("R6.eyJzb3VyY2UiOiJjb21peC50byIsImNoIjoiMTc5In0");

            // language → TranslatedLanguage.
            release.TranslatedLanguage.Should().Be("en");

            // scanlationGroup preserved.
            release.ScanlationGroup.Should().Be("Team Lumikha");

            // publishDate set.
            release.PublishDate.Should().Be(new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc).ToUniversalTime());

            // DownloadProtocol.Http for the gateway path.
            release.DownloadProtocol.Should().Be(DownloadProtocol.Http);

            // The parser itself does NOT de-dup — both duplicate-guid rows are present here.
            // (Plan 04's Fetch via CleanupReleases collapses them by Guid.)
            releases.Count(r => r.Guid == "comix.to:solo-leveling:179").Should().Be(2);
        }

        [Test]
        public void warning_records_failure_and_returns_releases()
        {
            var releases = Subject.ParseResponse(MakeResponse(_searchJson));

            // Each warnings[] entry → RecordFailure(sourceKey).
            Mocker.GetMock<IIndexerSourceStatusService>()
                .Verify(s => s.RecordFailure("comix.to", It.IsAny<TimeSpan>()), Times.Once());

            // The good releases are STILL returned (a warning never suppresses results).
            releases.Count.Should().BeGreaterThan(0);
        }

        [Test]
        public void warning_never_throws()
        {
            // A response carrying warnings[] must never throw out of ParseResponse.
            var act = () => Subject.ParseResponse(MakeResponse(_searchJson));
            act.Should().NotThrow();
        }

        [Test]
        public void recent_feed_parses_without_warnings()
        {
            // recent.json has no warnings[] → no RecordFailure call, releases returned.
            var releases = Subject.ParseResponse(MakeResponse(_recentJson));

            releases.Count.Should().Be(2);
            Mocker.GetMock<IIndexerSourceStatusService>()
                .Verify(s => s.RecordFailure(It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never());
        }

        private IndexerResponse MakeResponse(string content)
        {
            // IndexerRequest has (string, HttpAccept) and (HttpRequest) ctors but no
            // (HttpRequest, HttpAccept) overload — mirror MangaDexParserFixture's helper.
            var req = new HttpRequest("https://gateway.local/search");
            var resp = new HttpResponse(req, new HttpHeader(), content);
            return new IndexerResponse(new IndexerRequest(req), resp);
        }
    }
}
