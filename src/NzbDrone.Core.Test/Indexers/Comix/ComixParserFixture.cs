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
    ///
    /// <para>
    /// 2026-05-23 env-module-oracle cascade tests (added post commit e02af4cb2): the
    /// <c>ParseResponse_handles_unwrapped_chapter_list_shape</c>,
    /// <c>ParseResponse_handles_wrapped_chapter_list_shape_for_forward_compat</c>,
    /// <c>ParseResponse_handles_unwrapped_manga_list_shape</c>, and
    /// <c>ParseResponse_handles_wrapped_manga_list_shape_for_forward_compat</c> tests below
    /// lock the dual-shape contract. The pre-fix parser only probed
    /// <c>envelope["result"]["items"]</c> (wrapped) and silently produced 0 releases when
    /// the env-module-oracle started returning the UNWRAPPED inner shape
    /// (<c>{items:[...], meta:...}</c>) on 2026-05-23. The cascade with
    /// ResolveMangaHashAsync's plain-GET 403 produced 0 Comix results on interactive
    /// search even though comix.to had the title — the existing live fixtures only
    /// asserted the raw signer body contained the substring "items" so the downstream
    /// shape break was invisible.
    /// </para>
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

        // ──────────────────────────────────────────────────────────────────────────────
        // env-module-oracle cascade coverage (commit e02af4cb2).
        //
        // Catches the silent-zero-releases regression where the parser probed only the
        // pre-Phase-3 wrapped envelope shape (envelope["result"]["items"]) while the
        // 2026-05-23 env-module-oracle started returning the UNWRAPPED inner shape
        // ({items, meta}) because the bundle's ok+result interceptor strips the
        // {status:"ok", result:{...}} wrapper. Pre-fix, the parser saw null result.items
        // and emitted 0 releases — interactive search produced 0 Comix rows even when
        // the signer call succeeded. The legacy live fixture only asserted the body
        // contained the substring "items" so the shape break was invisible.
        // ──────────────────────────────────────────────────────────────────────────────

        [Test]
        public void ParseResponse_handles_unwrapped_chapter_list_shape()
        {
            // env-module-oracle (Phase 3 default; 2026-05-23+) — the bundle's ok+result
            // interceptor strips the {status, result:{...}} envelope so chapter rows
            // live at the JSON root under `items`.
            var unwrapped = @"{
                ""items"": [
                    { ""id"": 9573393, ""hid"": ""ch-22-h"", ""number"": 22, ""name"": ""The Greatest Estate Developer 22"", ""title"": null, ""updatedAt"": ""2026-05-01T00:00:00.000000Z"", ""publishedAt"": null, ""group"": { ""id"": 403, ""name"": ""Thunderscans"", ""slug"": ""thunder"" }, ""isOfficial"": 0, ""language"": ""en"" },
                    { ""id"": 9573394, ""hid"": ""ch-23-h"", ""number"": 23, ""name"": ""The Greatest Estate Developer 23"", ""title"": null, ""updatedAt"": ""2026-05-02T00:00:00.000000Z"", ""publishedAt"": null, ""group"": null, ""isOfficial"": 1, ""language"": ""en"" }
                ],
                ""meta"": { ""current_page"": 1, ""last_page"": 1 }
            }";

            var releases = Subject.ParseResponse(MakeResponse(unwrapped));

            releases.Should().NotBeEmpty("env-module-oracle unwrapped shape must produce releases — the pre-fix parser silently produced 0 releases here, masking the cascade bug commit e02af4cb2 fixed.");
            releases.Should().HaveCount(2);
            releases.Should().OnlyContain(r => r.DownloadProtocol == DownloadProtocol.Http);
        }

        [Test]
        public void ParseResponse_handles_wrapped_chapter_list_shape_for_forward_compat()
        {
            // Pre-Phase-3 wrapped envelope shape — preserved for forward compat across
            // rotations (e.g., if comix.to ever reverts the interceptor or a future
            // fallback path re-introduces the wrapper).
            var wrapped = @"{
                ""status"": ""ok"",
                ""result"": {
                    ""items"": [
                        { ""id"": 9573393, ""hid"": ""ch-22-h"", ""number"": 22, ""name"": ""The Greatest Estate Developer 22"", ""title"": null, ""updatedAt"": ""2026-05-01T00:00:00.000000Z"", ""publishedAt"": null, ""group"": null, ""isOfficial"": 1, ""language"": ""en"" }
                    ],
                    ""pagination"": { ""current_page"": 1, ""last_page"": 1 }
                }
            }";

            var releases = Subject.ParseResponse(MakeResponse(wrapped));

            releases.Should().NotBeEmpty("wrapped envelope must still parse — preserved for forward compat across rotations.");
            releases.Should().HaveCount(1);
        }

        [Test]
        public void ParseResponse_handles_unwrapped_manga_list_shape()
        {
            // 2026-05-23 cascade fix routes the keyword-search endpoint
            // (/api/v1/manga?keyword=...) through the env-module signer too — same
            // unwrapped shape. ComixIndexer.ResolveMangaHashAsync probes this body to
            // pick a hid; the parser also needs to handle it for symmetry on shape-
            // detection paths.
            var unwrappedManga = @"{
                ""items"": [
                    { ""id"": 116210, ""hid"": ""mr3m0"", ""title"": ""The Forgotten Field"", ""type"": ""manga"", ""originalLanguage"": ""en"", ""latestChapter"": 12, ""chapterUpdatedAt"": ""2026-05-01T00:00:00.000000Z"", ""status"": ""releasing"" }
                ],
                ""meta"": { ""current_page"": 1, ""last_page"": 1 }
            }";

            var releases = Subject.ParseResponse(MakeResponse(unwrappedManga));

            releases.Should().NotBeEmpty("env-module-oracle manga-list unwrapped shape must produce releases for the latest-updates / keyword-search paths.");
            releases.Should().Contain(r => r.Title.Contains("The Forgotten Field"));
        }

        [Test]
        public void ParseResponse_handles_wrapped_manga_list_shape_for_forward_compat()
        {
            // Forward-compat companion to the manga-list unwrapped test.
            var wrappedManga = @"{
                ""status"": ""ok"",
                ""result"": {
                    ""items"": [
                        { ""id"": 116210, ""hid"": ""mr3m0"", ""title"": ""The Forgotten Field"", ""type"": ""manga"", ""originalLanguage"": ""en"", ""latestChapter"": 12, ""chapterUpdatedAt"": ""2026-05-01T00:00:00.000000Z"", ""status"": ""releasing"" }
                    ]
                }
            }";

            var releases = Subject.ParseResponse(MakeResponse(wrappedManga));

            releases.Should().NotBeEmpty("wrapped manga-list shape must still parse — preserved for forward compat.");
        }

        private IndexerResponse MakeResponse(string content)
        {
            // Note: IndexerRequest has (string, HttpAccept) and (HttpRequest) ctors but no
            // (HttpRequest, HttpAccept) overload — Plan 03-01 scaffold typed this incorrectly.
            // Pre-existing bug surfaced when Plan 03-05 wired production types; repaired inline
            // (Rule 1 — bug) so Comix Wave 0 fixtures can compile and exercise the parser.
            // Mirrors the identical repair Plan 03-04 made to MangaDexParserFixture.cs.
            var req = new HttpRequest("https://comix.to/api/v1/manga/x/chapters");
            var resp = new HttpResponse(req, new HttpHeader(), content);
            return new IndexerResponse(new IndexerRequest(req), resp);
        }
    }
}
