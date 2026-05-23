using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Comix;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.Comix
{
    /// <summary>
    /// Wave 0 stub fixture for <see cref="ComixIndexer"/>. References NOT-YET-BUILT production
    /// types (lands in Plan 03-05). Mirrors <c>MangaDexIndexerFixture</c> shape; differences:
    ///
    /// - <c>SourceKey == "comix.to"</c> (D-12 verbatim site-name convention)
    /// - <see cref="ComixIndexer.GetDownloadHeaders"/> returns <c>Referer: https://comix.to/</c>
    ///   (D-14 — required by upstream comix.to API per keiyoushi extensions PR #11658)
    /// - NO UA-hidden reflection test (comix.to allows UA override per RESEARCH §anti-bot)
    /// </summary>
    [TestFixture]
    public class ComixIndexerFixture : CoreTest<ComixIndexer>
    {
        [SetUp]
        public void Setup()
        {
            Subject.Definition = new IndexerDefinition
            {
                Id = 2,
                Name = "Comix",
                Settings = new ComixIndexerSettings
                {
                    BaseUrl = "https://comix.to",
                    SourceKey = "comix.to"
                }
            };

            // Phase 17 D-16: Mock<IComixSigner> returning canned decoded JSON shape so
            // ComixIndexer.Fetch + ComixRequestGenerator.GetSearchRequests don't need a
            // live Chromium in unit tests. Wave 1 Plan 17-02 wires _signer through the
            // ComixIndexer constructor — this Mock prevents MissingDependencyException
            // when AutoMoqer resolves Subject after the new ctor signature lands.
            const string CannedManga = "{\"result\":{\"items\":[]}}";
            Mocker.GetMock<IComixSigner>()
                  .Setup(s => s.ProxyFetchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(CannedManga);
        }

        [Test]
        public void DefaultSourceKey_should_be_comix_to()
        {
            Subject.DefaultSourceKey.Should().Be("comix.to");
        }

        [Test]
        public void Protocol_should_be_Http()
        {
            Subject.Protocol.Should().Be(DownloadProtocol.Http);
        }

        [Test]
        public async Task Fetch_MangaSearchCriteria_calls_FetchReleases()
        {
            var criteria = new MangaSearchCriteria();
            var releases = await Subject.Fetch(criteria);
            releases.Should().NotBeNull();
        }

        [Test]
        public void GetDownloadHeaders_returns_Referer_for_comix()
        {
            // D-14: comix.to requires Referer header on chapter requests.
            var release = new ReleaseInfo();
            var headers = Subject.GetDownloadHeaders(release);
            headers.Should().ContainKey("Referer");
            headers["Referer"].Should().Be("https://comix.to/");
        }

        [Test]
        public void EnrichTitlesWithMangaName_prefixes_chapter_titles_lacking_manga_name()
        {
            // ComixParser.ParseChapterList emits titles like "Chapter 4 [en] [Group]" — the
            // chapter-list endpoint is keyed per-manga so the parent title is implicit. Without
            // a manga-name prefix, MangaParser.ParseChapterTitle can't extract a usable
            // MangaTitle and downstream lookup fails with "Unknown Manga".
            var releases = new System.Collections.Generic.List<ReleaseInfo>
            {
                new ReleaseInfo { Title = "Chapter 4 [en] [Thunderscans]" },
                new ReleaseInfo { Title = "Chapter 12.5 [en] [Asura Scans]" },
            };

            var enriched = ComixIndexer.EnrichTitlesWithMangaName(releases, "The Forgotten Field");

            enriched[0].Title.Should().Be("The Forgotten Field - Chapter 4 [en] [Thunderscans]");
            enriched[1].Title.Should().Be("The Forgotten Field - Chapter 12.5 [en] [Asura Scans]");
        }

        [Test]
        public void EnrichTitlesWithMangaName_skips_titles_already_carrying_manga_name()
        {
            // ParseMangaList path already prefixes the title (e.g., latest-updates feed). Don't
            // double-prefix when the manga name is already present.
            var releases = new System.Collections.Generic.List<ReleaseInfo>
            {
                new ReleaseInfo { Title = "The Forgotten Field - Chapter 7 [en]" },
            };

            var enriched = ComixIndexer.EnrichTitlesWithMangaName(releases, "The Forgotten Field");

            enriched[0].Title.Should().Be("The Forgotten Field - Chapter 7 [en]");
        }

        [Test]
        public void EnrichTitlesWithMangaName_no_op_when_manga_title_blank()
        {
            // Defensive: if the criteria carries no manga title (shouldn't happen post-Phase 6,
            // but keep the helper safe), leave the releases untouched.
            var releases = new System.Collections.Generic.List<ReleaseInfo>
            {
                new ReleaseInfo { Title = "Chapter 4 [en] [Group]" },
            };

            var enriched = ComixIndexer.EnrichTitlesWithMangaName(releases, null);

            enriched[0].Title.Should().Be("Chapter 4 [en] [Group]");
        }

        // ──────────────────────────────────────────────────────────────────────────────
        // env-module-oracle cascade integration coverage (commit e02af4cb2).
        //
        // This test is the integration counterpart to ComixParserFixture's per-shape
        // tests — it discriminates the signer mock by apiPath so the same Fetch call
        // exercises the hid-resolver leg AND the chapter-list-parser leg in one shot.
        //
        // Pre-fix failure mode the test catches:
        //   1. ResolveMangaHashAsync did plain GETs (Cloudflare 403 → caught silently);
        //      ResolvedSignerPaths ended up empty → 0 releases.
        //   2. ComixParser only probed envelope["result"]["items"] (wrapped); the env-
        //      module-oracle returns unwrapped {items, meta} → parser saw null → 0
        //      releases even if step 1 succeeded.
        //
        // Both legs compose into the user-reported "interactive search returned 0
        // Comix results for 'The Greatest Estate Developer'" bug. This Fetch-level test
        // would have caught it; the existing live fixtures only asserted the raw signer
        // body contained the substring "items" so the downstream cascade was invisible.
        // ──────────────────────────────────────────────────────────────────────────────

        [Test]
        public async Task Fetch_MangaSearchCriteria_produces_releases_when_signer_returns_unwrapped_env_module_shapes()
        {
            // Realistic env-module-oracle responses keyed by apiPath. The signer returns
            // the UNWRAPPED inner shape because the bundle's ok+result interceptor strips
            // the {status:"ok", result:{...}} envelope (Phase 3 default per env-module-
            // oracle pivot, 2026-05-23).
            const string unwrappedSearchHit = @"{
                ""items"": [
                    { ""id"": 116210, ""hid"": ""mr3m0"", ""title"": ""The Greatest Estate Developer"", ""type"": ""manga"", ""originalLanguage"": ""ko"", ""latestChapter"": 23, ""chapterUpdatedAt"": ""2026-05-02T00:00:00.000000Z"", ""status"": ""releasing"" }
                ],
                ""meta"": { ""current_page"": 1, ""last_page"": 1 }
            }";
            const string unwrappedChapterList = @"{
                ""items"": [
                    { ""id"": 9573393, ""hid"": ""ch-22-h"", ""number"": 22, ""name"": null, ""title"": null, ""updatedAt"": ""2026-05-01T00:00:00.000000Z"", ""publishedAt"": null, ""group"": { ""id"": 403, ""name"": ""Thunderscans"", ""slug"": ""thunder"" }, ""isOfficial"": 0, ""language"": ""en"" },
                    { ""id"": 9573394, ""hid"": ""ch-23-h"", ""number"": 23, ""name"": null, ""title"": null, ""updatedAt"": ""2026-05-02T00:00:00.000000Z"", ""publishedAt"": null, ""group"": null, ""isOfficial"": 1, ""language"": ""en"" }
                ],
                ""meta"": { ""current_page"": 1, ""last_page"": 1 }
            }";

            // Override the canned-empty SetUp mock with an apiPath-discriminating
            // responder so BOTH legs (hid-resolver + chapter-list-parser) exercise
            // realistic Phase-3 oracle responses.
            Mocker.GetMock<IComixSigner>()
                  .Setup(s => s.ProxyFetchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .Returns<string, CancellationToken>((path, _) =>
                  {
                      // ResolveMangaHashAsync builds: "/manga?keyword=...&limit=10"
                      if (path.StartsWith("/manga?keyword=", System.StringComparison.Ordinal))
                      {
                          return Task.FromResult(unwrappedSearchHit);
                      }

                      // ComixRequestGenerator.BuildChapterListPath builds:
                      // "/manga/{hid}/chapters?order%5Bnumber%5D=desc&limit=100&..."
                      if (path.StartsWith("/manga/mr3m0/chapters", System.StringComparison.Ordinal))
                      {
                          return Task.FromResult(unwrappedChapterList);
                      }

                      return Task.FromResult<string>(null);
                  });

            var criteria = new MangaSearchCriteria
            {
                Manga = new NzbDrone.Core.Manga.Manga
                {
                    Title = "The Greatest Estate Developer",
                    CleanTitle = "thegreatestestatedeveloper"
                }
            };

            var releases = await Subject.Fetch(criteria);

            // The cascade catch: pre-fix this returned an empty list because either:
            //   (a) hid-resolver swallowed a Cloudflare 403 and returned null hid, OR
            //   (b) parser failed to find items[] under envelope["result"]["items"].
            // Post-fix BOTH legs work and we get 2 chapter releases.
            releases.Should().NotBeNull();
            releases.Should().HaveCount(2,
                "env-module-oracle cascade: hid-resolver must dispatch through the signer AND parser must accept the unwrapped {items, meta} shape — the user-reported '0 Comix results for The Greatest Estate Developer' interactive-search bug surfaced when either leg silently emitted zero (cascade fix commit e02af4cb2).");

            // EnrichTitlesWithMangaName prefixes the manga title (chapter-list endpoint
            // is keyed per-manga so titles arrive without the parent name — without the
            // prefix, downstream MangaParser.ParseChapterTitle can't resolve the manga
            // and every release rejects with "Unknown Manga").
            releases.Should().OnlyContain(r => r.Title.StartsWith("The Greatest Estate Developer - "));
            releases.Should().Contain(r => r.Title.Contains("Chapter 22"));
            releases.Should().Contain(r => r.Title.Contains("Chapter 23"));

            // DownloadUrl shape: ComixParser builds /api/v1/chapters/{id} (no /pages
            // suffix per Phase 17.2 GAP-17-E winner verdict).
            releases.Should().OnlyContain(r => r.DownloadUrl != null && r.DownloadUrl.Contains("comix.to/api/v1/chapters/"));
            releases.Should().OnlyContain(r => r.DownloadProtocol == DownloadProtocol.Http);
        }

        [Test]
        public async Task Fetch_MangaSearchCriteria_returns_empty_when_signer_returns_empty_search_hit()
        {
            // When the keyword-search returns no items, ResolveMangaHashAsync returns
            // null hid, ResolvedSignerPaths is empty, and Fetch produces 0 releases.
            // This is the normal "manga not on comix.to" path — explicit so a future
            // regression doesn't silently fall through to a non-empty release list.
            const string unwrappedEmptySearch = @"{ ""items"": [], ""meta"": { ""current_page"": 1, ""last_page"": 1 } }";

            Mocker.GetMock<IComixSigner>()
                  .Setup(s => s.ProxyFetchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(unwrappedEmptySearch);

            var criteria = new MangaSearchCriteria
            {
                Manga = new NzbDrone.Core.Manga.Manga
                {
                    Title = "Manga That Definitely Does Not Exist On Comix",
                    CleanTitle = "mangathatdefinitelydoesnotexistoncomix"
                }
            };

            var releases = await Subject.Fetch(criteria);

            releases.Should().NotBeNull();
            releases.Should().BeEmpty("zero-hit keyword-search must produce zero releases; no fallthrough to chapter-list dispatch.");
        }
    }
}
