using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.MyAnimeListStack;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests.MyAnimeListStack
{
    // Quick task 260608-vf9 Task 2 — unit tier for MyAnimeListStackImportList.
    //
    // Pinned to two CAPTURED HTML fixtures (real MAL stack pages, scripts/styles stripped):
    //   * Files/ImportLists/MyAnimeListStack/manga_stack.html — real /stacks/85344 (6 manga
    //     add-buttons via selected_manga_id, 0 anime add-buttons, plus an unrelated /anime/
    //     sidebar distractor link to prove the parser does NOT false-positive on raw /anime/).
    //   * Files/ImportLists/MyAnimeListStack/anime_stack.html — real /stacks/85261 (13 anime
    //     add-buttons via selected_series_id, 0 manga add-buttons).
    //
    // No live HTTP — Mocker stubs IHttpClient + drives Subject.Fetch() / Subject.Test().
    [TestFixture]
    public class MyAnimeListStackImportListFixture : CoreTest<MyAnimeListStackImportList>
    {
        // A raw /anime/ sidebar link in the manga fixture (NOT an add-button) — must NEVER be
        // imported as a manga item.
        private const int AnimeDistractorId = 52991;

        // The six manga MAL ids present as add-buttons in the captured manga_stack.html fixture.
        private static readonly int[] ExpectedMangaIds = { 169504, 143325, 88717, 174650, 134649, 93610 };

        private Mock<IHttpClient> _httpClient;
        private Mock<IImportListStatusService> _statusService;

        [SetUp]
        public void Setup()
        {
            _httpClient = Mocker.GetMock<IHttpClient>();
            _statusService = Mocker.GetMock<IImportListStatusService>();
            _ = Mocker.GetMock<IConfigService>();
            _ = Mocker.GetMock<IMangaParsingService>();
            _ = Mocker.GetMock<ILocalizationService>();

            _statusService.Setup(s => s.GetBlockedProviders())
                          .Returns(new List<ImportListStatus>());

            Subject.Definition = new ImportListDefinition
            {
                Id = 7,
                Name = "MAL Stack (fixture)",
                Settings = new MyAnimeListStackImportListSettings { StackUrl = "85344" }
            };
        }

        private void GivenResponseHtml(string html)
        {
            _httpClient.Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                       .Returns<HttpRequest>(req =>
                           new HttpResponse(req, new HttpHeader { ContentType = "text/html" }, html, HttpStatusCode.OK));
        }

        // ── Test 1: manga extraction + MalId (user's explicitly-required assertion) ──────────
        [Test]
        public void fetch_manga_stack_extracts_items_with_malid()
        {
            GivenResponseHtml(ReadAllText("Files/ImportLists/MyAnimeListStack/manga_stack.html"));

            var result = Subject.Fetch();

            result.Should().NotBeNull();
            result.AnyFailure.Should().BeFalse();

            // One ImportListItemInfo per manga add-button (6 in the captured fixture).
            result.Manga.Should().HaveCount(ExpectedMangaIds.Length);

            // Every item carries a positive MalId, and the set matches the fixture ids exactly.
            result.Manga.Should().OnlyContain(m => m.MalId.HasValue && m.MalId.Value > 0);
            result.Manga.Select(m => m.MalId.Value).Should().BeEquivalentTo(ExpectedMangaIds);

            // The raw /anime/ sidebar distractor must NOT have been imported.
            result.Manga.Select(m => m.MalId.Value).Should().NotContain(AnimeDistractorId);

            // At least one Title is the de-underscored slug (169504 → "Dangerous Obsessions").
            result.Manga.Should().Contain(m => m.MalId == 169504 && m.Title == "Dangerous Obsessions");
        }

        // ── Test 2: anime-stack rejection (user's hard requirement) ──────────────────────────
        [Test]
        public void test_rejects_anime_stack()
        {
            GivenResponseHtml(ReadAllText("Files/ImportLists/MyAnimeListStack/anime_stack.html"));

            var r = Subject.Test();

            r.IsValid.Should().BeFalse();
            r.Errors.Should().Contain(e => e.ErrorMessage.Contains("Anime stack"),
                "the user's hard requirement: an Anime stack must be rejected on save with a clear message.");
        }

        // ── Test 3: a manga stack passes Test ────────────────────────────────────────────────
        [Test]
        public void test_passes_manga_stack()
        {
            GivenResponseHtml(ReadAllText("Files/ImportLists/MyAnimeListStack/manga_stack.html"));

            Subject.Test().IsValid.Should().BeTrue();
        }

        // ── Test 4: empty / non-stack page is rejected with the "no manga" message ───────────
        [Test]
        public void test_rejects_empty_or_non_stack_page()
        {
            GivenResponseHtml("<html><body>nope</body></html>");

            var r = Subject.Test();

            r.IsValid.Should().BeFalse();
            r.Errors.Should().Contain(e => e.ErrorMessage.Contains("No manga found"));
        }

        // ── Test 5: SSRF-mitigation contract — URL and bare id rebuild to the same host ──────
        [Test]
        public void request_generator_normalizes_url_and_bare_id()
        {
            const string canonical = "https://myanimelist.net/stacks/85344";

            var fromUrl = new MyAnimeListStackImportListRequestGenerator
            {
                Settings = new MyAnimeListStackImportListSettings { StackUrl = "https://myanimelist.net/stacks/85344" }
            };

            var fromBareId = new MyAnimeListStackImportListRequestGenerator
            {
                Settings = new MyAnimeListStackImportListSettings { StackUrl = "85344" }
            };

            var urlRequest = fromUrl.GetListItems().GetAllTiers().First().First();
            var bareRequest = fromBareId.GetListItems().GetAllTiers().First().First();

            urlRequest.HttpRequest.Url.ToString().Should().Be(canonical);
            bareRequest.HttpRequest.Url.ToString().Should().Be(canonical);
        }
    }
}
