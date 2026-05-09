using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Mangarr.Api.V5.Manga.Chapter;
using Mangarr.Api.V5.Manga.Wanted;
using Mangarr.Http;
using Mangarr.Http.REST;
using Microsoft.AspNetCore.Http.HttpResults;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.MediaFiles.MangaImport;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.SignalR;
using NzbDrone.Test.Common;
namespace NzbDrone.Api.Test.Manga.Wanted
{
    // Sonarr divergence: NEW manga V5 controller fixture per Phase 6 Plan 06-09 — see DIVERGENCE.md.
    // Phase-12 follow-up (F-MISSING-SIGNALR closure, 2026-05-06) authored this fixture from
    // scratch when MangaMissingController was refactored from plain `Controller` →
    // `RestControllerWithSignalR<MissingChapterResource, Chapter>` + IHandle subscriptions for
    // ChapterGrabbedEvent / ChapterImportedEvent / ChapterFileDeletedEvent. Mirrors the
    // MangaCutoffControllerFixture pattern verbatim (commit 6ae1e771d) for cross-controller
    // consistency between the two manga V5 wanted endpoints.
    //
    // Phase-12 follow-up (canonical-resource-reuse, 2026-05-06) refactored every assertion that
    // referenced the now-deleted `MissingChapterResource` to consume the canonical
    // `ChapterResource` instead — mirrors TV's `MissingControllerFixture` consuming
    // `EpisodeResource` (no custom resource class). The `bool includeManga` query was likewise
    // replaced with the `MangaMissingSubresource[]? includeSubresources` enum-array shape (TV
    // peer pattern).
    //
    // Role-match analog: src/NzbDrone.Api.Test/Manga/Wanted/MangaCutoffControllerFixture.cs
    // (the immediate sibling — same shape, same SetUp, same five SignalR-broadcast tests +
    // route literal pin + filter/hydration assertions adapted to MangaMissingController's
    // ChapterResource + IChapterService.ChaptersWithoutFiles surface).
    //
    // Fixture lives under NzbDrone.Api.Test (NOT NzbDrone.Core.Test) because Sonarr.Core.Test does not
    // project-reference Mangarr.Api.V5; Sonarr.Api.Test does. Same convention as
    // src/NzbDrone.Api.Test/Manga/MangaControllerSignalRFixture.cs (Plan 10-05 Rule 3 deviation —
    // documented in src/Mangarr.Api.V5/Manga/CLAUDE.md lines 108-111) and the cutoff sibling.
    //
    // Per-plan unit-test filter: dotnet test --filter "FullyQualifiedName~MangaMissing" must
    // return at least 1 passing test. This fixture provides:
    //   1. Happy-path paged GET delegates to IChapterService.ChaptersWithoutFiles exactly once.
    //   2. Reflective Attribute lookup confirms route literal "manga/wanted/missing" — the contract
    //      that the frontend Plan 07-10 fetch path expects per Plan 07-02 URL-shaped key contract.
    //   3. Filter-expression behavior tests (monitored / mangaIds / languages — defense-in-depth
    //      mirroring 12-REVIEW MED-01 pattern).
    //   4. Subresource hydration test (includeSubresources=[Manga] / null / empty array).
    //   5. (F-MISSING-SIGNALR) Base-class assertion: MangaMissingController extends
    //      RestControllerWithSignalR<ChapterResource, Chapter> (NOT plain Controller). This
    //      pin protects against silent regression to the plain-Controller shape.
    //   6. (F-MISSING-SIGNALR) IHandle<ChapterGrabbedEvent> broadcasts Updated for each chapter
    //      id in the RemoteChapter.Chapters list.
    //   7. (F-MISSING-SIGNALR) IHandle<ChapterImportedEvent> broadcasts Updated for the imported
    //      chapter's id (single Chapter per event — Plan 06-07 Pitfall 4 ordering).
    //   8. (F-MISSING-SIGNALR) IHandle<ChapterFileDeletedEvent> broadcasts Updated for the
    //      deleted file's ChapterId.
    //   9. (F-MISSING-SIGNALR) IHandle<ChapterFileDeletedEvent> Upgrade-reason short-circuit
    //      mirrors MangaController + MangaCutoffController precedent (no broadcast on Upgrade
    //      — the upcoming Add fires next).
    //
    // BroadcastResourceChange path (RestControllerWithSignalR.cs:52-67) requires
    // IBroadcastSignalRMessage.IsConnected to be true AND requires the controller's
    // GetResourceById(int) override to return a non-null resource — SetUp wires both.
    [TestFixture]
    public class MangaMissingControllerFixture : TestBase<MangaMissingController>
    {
        [SetUp]
        public void Setup()
        {
            // RestControllerWithSignalR short-circuits BroadcastMessage when IsConnected
            // is false. Force true so the Mocker observes the BroadcastMessage call in
            // the IHandle<...> tests below. Existing happy-path / filter / hydration tests
            // do not exercise the broadcast path, so this SetUp is a no-op for them.
            Mocker.GetMock<IBroadcastSignalRMessage>()
                  .SetupGet(b => b.IsConnected)
                  .Returns(true);

            // BroadcastResourceChange(ModelAction, int) round-trips through
            // GetResourceById -> _chapterService.GetChapter(id). Default mock returns null,
            // which would NRE when ToResource() runs against a null Chapter. Provide a default
            // non-null chapter for any id so the broadcast reaches BroadcastMessage. Per-test
            // Setups can override with .Setup(s => s.GetChapter(specificId)) for
            // id-binding assertions.
            Mocker.GetMock<IChapterService>()
                  .Setup(s => s.GetChapter(It.IsAny<int>()))
                  .Returns<int>(id => new NzbDrone.Core.Manga.Chapter
                  {
                      Id = id,
                      MangaId = 42,
                      ChapterNumber = 1m,
                      Monitored = true,
                      ChapterType = ChapterType.Regular
                  });
        }

        [Test]
        public void GetMissingChapters_delegates_to_chapter_service()
        {
            var chapters = new List<NzbDrone.Core.Manga.Chapter>
            {
                new() { Id = 1, MangaId = 42, ChapterNumber = 1m, Monitored = true, ChapterType = ChapterType.Regular },
                new() { Id = 2, MangaId = 42, ChapterNumber = 2m, Monitored = true, ChapterType = ChapterType.Regular },
            };

            Mocker.GetMock<IChapterService>()
                .Setup(s => s.ChaptersWithoutFiles(It.IsAny<PagingSpec<NzbDrone.Core.Manga.Chapter>>()))
                .Returns<PagingSpec<NzbDrone.Core.Manga.Chapter>>(spec =>
                {
                    spec.Records = chapters;
                    spec.TotalRecords = chapters.Count;
                    return spec;
                });

            var result = Subject.GetMissingChapters(new PagingRequestResource());

            result.Should().BeOfType<Ok<PagingResource<ChapterResource>>>();
            var ok = (Ok<PagingResource<ChapterResource>>)result;
            ok.Value!.Records.Should().HaveCount(2);

            Mocker.GetMock<IChapterService>()
                .Verify(s => s.ChaptersWithoutFiles(It.IsAny<PagingSpec<NzbDrone.Core.Manga.Chapter>>()),
                    Times.Once);
        }

        [Test]
        public void Route_attribute_is_manga_wanted_missing_literal_per_plan_07_02_contract()
        {
            // Plan 07-02 URL-shaped React Query key contract — the route attribute string is the
            // load-bearing contract between the frontend useMissing hook (Plan 07-10) and this
            // controller. Mismatched route literal silently breaks every fetch from
            // /manga/wanted/missing. Pitfall 5 — TV/manga cache MUST NOT collide.
            var attr = (V5ApiControllerAttribute)Attribute.GetCustomAttribute(
                typeof(MangaMissingController), typeof(V5ApiControllerAttribute));

            attr.Should().NotBeNull();
            attr.Resource.Should().Be("manga/wanted/missing");
        }

        // Defense-in-depth tests asserting the observable controller behaviors the route-attribute +
        // happy-path tests do not exercise (mirrors MangaCutoffControllerFixture's 12-REVIEW MED-01
        // pattern):
        //   * `monitored=true` adds a `c.Monitored == true` FilterExpression
        //   * `mangaIds[]` (non-empty) adds a `mangaIds.Contains(c.MangaId)` FilterExpression
        //   * `languages[]` (non-empty) adds a `languages.Contains(c.TranslatedLanguage)` FilterExpression
        //   * `includeSubresources=[Manga]` triggers IMangaService.GetManga(MangaId) hydration in MapToResource
        //   * `includeSubresources` null / empty does NOT trigger hydration

        [Test]
        public void GetMissingChapters_applies_monitored_filter_when_monitored_true()
        {
            PagingSpec<NzbDrone.Core.Manga.Chapter> capturedSpec = null;

            Mocker.GetMock<IChapterService>()
                .Setup(s => s.ChaptersWithoutFiles(It.IsAny<PagingSpec<NzbDrone.Core.Manga.Chapter>>()))
                .Returns<PagingSpec<NzbDrone.Core.Manga.Chapter>>(spec =>
                {
                    capturedSpec = spec;
                    spec.Records = new List<NzbDrone.Core.Manga.Chapter>();
                    spec.TotalRecords = 0;
                    return spec;
                });

            Subject.GetMissingChapters(new PagingRequestResource(), monitored: true);

            capturedSpec.Should().NotBeNull();
            capturedSpec!.FilterExpressions.Should().NotBeEmpty();

            // Compile + invoke the captured filter expressions against synthetic Chapter rows
            // to verify the monitored filter actually selects only Monitored == true rows.
            var monitoredChapter = new NzbDrone.Core.Manga.Chapter { Monitored = true };
            var unmonitoredChapter = new NzbDrone.Core.Manga.Chapter { Monitored = false };

            var allFiltersPass = capturedSpec.FilterExpressions
                .Select(e => e.Compile())
                .ToList();

            allFiltersPass.All(f => f(monitoredChapter)).Should().BeTrue(
                "monitored=true filter should accept Monitored chapter rows");
            allFiltersPass.Any(f => !f(unmonitoredChapter)).Should().BeTrue(
                "monitored=true filter should reject Unmonitored chapter rows");
        }

        [Test]
        public void GetMissingChapters_omits_monitored_filter_when_monitored_false()
        {
            PagingSpec<NzbDrone.Core.Manga.Chapter> capturedSpec = null;

            Mocker.GetMock<IChapterService>()
                .Setup(s => s.ChaptersWithoutFiles(It.IsAny<PagingSpec<NzbDrone.Core.Manga.Chapter>>()))
                .Returns<PagingSpec<NzbDrone.Core.Manga.Chapter>>(spec =>
                {
                    capturedSpec = spec;
                    spec.Records = new List<NzbDrone.Core.Manga.Chapter>();
                    spec.TotalRecords = 0;
                    return spec;
                });

            // monitored=false short-circuits the `if (monitored)` branch — no Monitored
            // FilterExpression is added, so unmonitored chapters surface in the result.
            Subject.GetMissingChapters(new PagingRequestResource(), monitored: false);

            capturedSpec.Should().NotBeNull();

            // With no other filters supplied, FilterExpressions must be empty.
            capturedSpec!.FilterExpressions.Should().BeEmpty(
                "monitored=false should not add any FilterExpression");
        }

        [Test]
        public void GetMissingChapters_applies_mangaIds_filter_when_mangaIds_non_empty()
        {
            PagingSpec<NzbDrone.Core.Manga.Chapter> capturedSpec = null;

            Mocker.GetMock<IChapterService>()
                .Setup(s => s.ChaptersWithoutFiles(It.IsAny<PagingSpec<NzbDrone.Core.Manga.Chapter>>()))
                .Returns<PagingSpec<NzbDrone.Core.Manga.Chapter>>(spec =>
                {
                    capturedSpec = spec;
                    spec.Records = new List<NzbDrone.Core.Manga.Chapter>();
                    spec.TotalRecords = 0;
                    return spec;
                });

            // monitored=false to isolate the mangaIds filter.
            Subject.GetMissingChapters(new PagingRequestResource(), monitored: false, mangaIds: new[] { 42, 99 });

            capturedSpec.Should().NotBeNull();
            capturedSpec!.FilterExpressions.Should().HaveCount(1,
                "mangaIds filter should add exactly one FilterExpression when monitored=false");

            // Compile + invoke the captured mangaIds filter against synthetic Chapter rows.
            var matchingChapter = new NzbDrone.Core.Manga.Chapter { MangaId = 42 };
            var nonMatchingChapter = new NzbDrone.Core.Manga.Chapter { MangaId = 7 };

            var compiledFilter = capturedSpec.FilterExpressions[0].Compile();
            compiledFilter(matchingChapter).Should().BeTrue(
                "mangaIds filter should accept rows whose MangaId is in the supplied list");
            compiledFilter(nonMatchingChapter).Should().BeFalse(
                "mangaIds filter should reject rows whose MangaId is not in the supplied list");
        }

        // Sonarr divergence: Phase 16 STRUCT-06 + D-04 — languages filter joins ChapterRelease;
        // zero-release IS Missing in unfiltered view but EXCLUDED from language-filtered subset.
        // Plan 16-04 retarget: the languages filter now operates post-paged in-memory against
        // bulk-loaded ChapterReleases (IChapterReleaseService.GetReleasesByChapterIds — single
        // SQL, N+1-safe). A chapter is "in" the filter if ANY of its ChapterReleases match
        // any language in the filter (case-insensitive — BCP-47 codes).
        [Test]
        public void GetMissingChapters_applies_languages_filter_when_languages_non_empty()
        {
            // Arrange: 3 chapters, each with releases:
            //   - Chapter 1: en (MangaPlus)
            //   - Chapter 2: es (MangaPlus)
            //   - Chapter 3: ja (MangaPlus)
            // Filter ?languages=en,es should return Chapters 1 + 2 only.
            var chapters = new List<NzbDrone.Core.Manga.Chapter>
            {
                new() { Id = 1, MangaId = 42, ChapterNumber = 1m, Monitored = true, ChapterType = ChapterType.Regular },
                new() { Id = 2, MangaId = 42, ChapterNumber = 2m, Monitored = true, ChapterType = ChapterType.Regular },
                new() { Id = 3, MangaId = 42, ChapterNumber = 3m, Monitored = true, ChapterType = ChapterType.Regular },
            };

            Mocker.GetMock<IChapterService>()
                .Setup(s => s.ChaptersWithoutFiles(It.IsAny<PagingSpec<NzbDrone.Core.Manga.Chapter>>()))
                .Returns<PagingSpec<NzbDrone.Core.Manga.Chapter>>(spec =>
                {
                    spec.Records = chapters;
                    spec.TotalRecords = chapters.Count;
                    return spec;
                });

            Mocker.GetMock<IChapterReleaseService>()
                .Setup(s => s.GetReleasesByChapterIds(It.IsAny<List<int>>()))
                .Returns(new List<ChapterRelease>
                {
                    new() { Id = 10, ChapterId = 1, TranslatedLanguage = "en", ScanlationGroup = "MangaPlus" },
                    new() { Id = 11, ChapterId = 2, TranslatedLanguage = "es", ScanlationGroup = "MangaPlus" },
                    new() { Id = 12, ChapterId = 3, TranslatedLanguage = "ja", ScanlationGroup = "MangaPlus" },
                });

            var result = Subject.GetMissingChapters(
                new PagingRequestResource(),
                monitored: false,
                languages: new[] { "en", "es" });

            result.Should().BeOfType<Ok<PagingResource<ChapterResource>>>();
            var ok = (Ok<PagingResource<ChapterResource>>)result;
            ok.Value!.Records.Select(r => r.Id).Should().BeEquivalentTo(new[] { 1, 2 });

            // Verify N+1-safe path: single bulk call (NOT per-chapter GetReleasesByChapter).
            Mocker.GetMock<IChapterReleaseService>()
                .Verify(s => s.GetReleasesByChapterIds(It.IsAny<List<int>>()), Times.Once);
            Mocker.GetMock<IChapterReleaseService>()
                .Verify(s => s.GetReleasesByChapter(It.IsAny<int>()), Times.Never);
        }

        // Sonarr divergence: Phase 16 D-04 — Chapter with zero ChapterRelease rows IS in the
        // missing list when the languages filter is unset. The languages filter narrows further
        // (zero-release chapters are excluded from the filtered subset because they have no
        // language to match — see Multi_release_candidate_with_unsupported_language_is_rejected
        // in the spec fixture for the dual gate).
        [Test]
        public void GetMissingChapters_with_zero_releases_appears_when_languages_filter_unset()
        {
            // Arrange: monitored chapter with NO ChapterRelease rows. D-04: surfaces in missing list.
            var chapters = new List<NzbDrone.Core.Manga.Chapter>
            {
                new() { Id = 5, MangaId = 42, ChapterNumber = 5m, Monitored = true, ChapterType = ChapterType.Regular },
            };

            Mocker.GetMock<IChapterService>()
                .Setup(s => s.ChaptersWithoutFiles(It.IsAny<PagingSpec<NzbDrone.Core.Manga.Chapter>>()))
                .Returns<PagingSpec<NzbDrone.Core.Manga.Chapter>>(spec =>
                {
                    spec.Records = chapters;
                    spec.TotalRecords = chapters.Count;
                    return spec;
                });

            // No languages filter: the languages-filter branch is short-circuited and
            // GetReleasesByChapterIds is never called.
            var result = Subject.GetMissingChapters(new PagingRequestResource(), monitored: false);

            result.Should().BeOfType<Ok<PagingResource<ChapterResource>>>();
            var ok = (Ok<PagingResource<ChapterResource>>)result;
            ok.Value!.Records.Should().NotBeEmpty(
                "zero-release Chapter must surface in unfiltered missing list per D-04");
            ok.Value.Records.Single().Id.Should().Be(5);

            Mocker.GetMock<IChapterReleaseService>()
                .Verify(
                    s => s.GetReleasesByChapterIds(It.IsAny<List<int>>()),
                    Times.Never,
                    "languages filter unset: must NOT trigger ChapterRelease bulk-load");
        }

        [Test]
        public void GetMissingChapters_with_zero_releases_excluded_when_languages_filter_set()
        {
            // Arrange: monitored chapter with NO ChapterRelease rows. With languages filter
            // SET (?languages=en), zero-release chapters are EXCLUDED — they have no language
            // to match. Pairs with the unfiltered case above to lock both gates of D-04.
            var chapters = new List<NzbDrone.Core.Manga.Chapter>
            {
                new() { Id = 5, MangaId = 42, ChapterNumber = 5m, Monitored = true, ChapterType = ChapterType.Regular },
            };

            Mocker.GetMock<IChapterService>()
                .Setup(s => s.ChaptersWithoutFiles(It.IsAny<PagingSpec<NzbDrone.Core.Manga.Chapter>>()))
                .Returns<PagingSpec<NzbDrone.Core.Manga.Chapter>>(spec =>
                {
                    spec.Records = chapters;
                    spec.TotalRecords = chapters.Count;
                    return spec;
                });

            // Bulk lookup returns empty for this chapter id (zero-release case).
            Mocker.GetMock<IChapterReleaseService>()
                .Setup(s => s.GetReleasesByChapterIds(It.IsAny<List<int>>()))
                .Returns(new List<ChapterRelease>());

            var result = Subject.GetMissingChapters(
                new PagingRequestResource(),
                monitored: false,
                languages: new[] { "en" });

            result.Should().BeOfType<Ok<PagingResource<ChapterResource>>>();
            var ok = (Ok<PagingResource<ChapterResource>>)result;
            ok.Value!.Records.Should().BeEmpty(
                "zero-release Chapter must be excluded when languages filter is set per D-04 dual gate");
        }

        [Test]
        public void GetMissingChapters_languages_filter_is_case_insensitive()
        {
            // BCP-47 language codes are case-insensitive in practice. Filter "EN" should match
            // ChapterRelease.TranslatedLanguage = "en". Mirrors HashSet OrdinalIgnoreCase shape
            // in MangaMissingController.
            var chapters = new List<NzbDrone.Core.Manga.Chapter>
            {
                new() { Id = 1, MangaId = 42, ChapterNumber = 1m, Monitored = true, ChapterType = ChapterType.Regular },
            };

            Mocker.GetMock<IChapterService>()
                .Setup(s => s.ChaptersWithoutFiles(It.IsAny<PagingSpec<NzbDrone.Core.Manga.Chapter>>()))
                .Returns<PagingSpec<NzbDrone.Core.Manga.Chapter>>(spec =>
                {
                    spec.Records = chapters;
                    spec.TotalRecords = chapters.Count;
                    return spec;
                });

            Mocker.GetMock<IChapterReleaseService>()
                .Setup(s => s.GetReleasesByChapterIds(It.IsAny<List<int>>()))
                .Returns(new List<ChapterRelease>
                {
                    new() { Id = 10, ChapterId = 1, TranslatedLanguage = "en" },
                });

            var result = Subject.GetMissingChapters(
                new PagingRequestResource(),
                monitored: false,
                languages: new[] { "EN" });

            result.Should().BeOfType<Ok<PagingResource<ChapterResource>>>();
            var ok = (Ok<PagingResource<ChapterResource>>)result;
            ok.Value!.Records.Should().HaveCount(1, "case-insensitive match: 'EN' filter accepts 'en' release");
        }

        [Test]
        public void GetMissingChapters_hydrates_Manga_subresource_when_includeSubresources_contains_Manga()
        {
            var chapters = new List<NzbDrone.Core.Manga.Chapter>
            {
                new() { Id = 1, MangaId = 42, ChapterNumber = 1m, Monitored = true, ChapterType = ChapterType.Regular },
            };

            Mocker.GetMock<IChapterService>()
                .Setup(s => s.ChaptersWithoutFiles(It.IsAny<PagingSpec<NzbDrone.Core.Manga.Chapter>>()))
                .Returns<PagingSpec<NzbDrone.Core.Manga.Chapter>>(spec =>
                {
                    spec.Records = chapters;
                    spec.TotalRecords = chapters.Count;
                    return spec;
                });

            Mocker.GetMock<IMangaService>()
                .Setup(s => s.GetManga(42))
                .Returns(new NzbDrone.Core.Manga.Manga { Id = 42, Title = "Test Manga" });

            // Canonical-resource-reuse follow-up (2026-05-06): use TV-mirroring enum-array shape
            // `includeSubresources=[Manga]` instead of the original `bool includeManga = true`.
            var result = Subject.GetMissingChapters(
                new PagingRequestResource(),
                includeSubresources: new[] { MangaMissingSubresource.Manga });

            // IMangaService.GetManga must be called exactly once for the single chapter row.
            Mocker.GetMock<IMangaService>()
                .Verify(s => s.GetManga(42), Times.Once);

            result.Should().BeOfType<Ok<PagingResource<ChapterResource>>>();
            var ok = (Ok<PagingResource<ChapterResource>>)result;

            // The Manga subresource must be populated on each row with {Id, Title}.
            var record = ok.Value!.Records.Single();
            record.Manga.Should().NotBeNull();
            record.Manga!.Id.Should().Be(42);
            record.Manga.Title.Should().Be("Test Manga");
        }

        [Test]
        public void GetMissingChapters_skips_Manga_subresource_hydration_when_includeSubresources_null()
        {
            var chapters = new List<NzbDrone.Core.Manga.Chapter>
            {
                new() { Id = 1, MangaId = 42, ChapterNumber = 1m, Monitored = true, ChapterType = ChapterType.Regular },
            };

            Mocker.GetMock<IChapterService>()
                .Setup(s => s.ChaptersWithoutFiles(It.IsAny<PagingSpec<NzbDrone.Core.Manga.Chapter>>()))
                .Returns<PagingSpec<NzbDrone.Core.Manga.Chapter>>(spec =>
                {
                    spec.Records = chapters;
                    spec.TotalRecords = chapters.Count;
                    return spec;
                });

            // Default includeSubresources = null (no explicit arg).
            var result = Subject.GetMissingChapters(new PagingRequestResource());

            Mocker.GetMock<IMangaService>()
                .Verify(
                    s => s.GetManga(It.IsAny<int>()),
                    Times.Never,
                    "includeSubresources=null must NOT trigger IMangaService.GetManga hydration");

            result.Should().BeOfType<Ok<PagingResource<ChapterResource>>>();
            var ok = (Ok<PagingResource<ChapterResource>>)result;
            ok.Value!.Records.Single().Manga.Should().BeNull(
                "Manga subresource must remain null when includeSubresources is null");
        }

        [Test]
        public void GetMissingChapters_skips_Manga_subresource_hydration_when_includeSubresources_empty_array()
        {
            // Canonical-resource-reuse follow-up (2026-05-06): explicit-empty-array branch
            // exercises the `?? false` fallback inside `includeSubresources?.Contains(Manga) ?? false`
            // (TV `MissingController.GetMissingEpisodes` follows the same shape).
            var chapters = new List<NzbDrone.Core.Manga.Chapter>
            {
                new() { Id = 1, MangaId = 42, ChapterNumber = 1m, Monitored = true, ChapterType = ChapterType.Regular },
            };

            Mocker.GetMock<IChapterService>()
                .Setup(s => s.ChaptersWithoutFiles(It.IsAny<PagingSpec<NzbDrone.Core.Manga.Chapter>>()))
                .Returns<PagingSpec<NzbDrone.Core.Manga.Chapter>>(spec =>
                {
                    spec.Records = chapters;
                    spec.TotalRecords = chapters.Count;
                    return spec;
                });

            var result = Subject.GetMissingChapters(
                new PagingRequestResource(),
                includeSubresources: Array.Empty<MangaMissingSubresource>());

            Mocker.GetMock<IMangaService>()
                .Verify(
                    s => s.GetManga(It.IsAny<int>()),
                    Times.Never,
                    "includeSubresources=[] must NOT trigger IMangaService.GetManga hydration");

            result.Should().BeOfType<Ok<PagingResource<ChapterResource>>>();
            var ok = (Ok<PagingResource<ChapterResource>>)result;
            ok.Value!.Records.Single().Manga.Should().BeNull(
                "Manga subresource must remain null when includeSubresources is an empty array");
        }

        // ===================== F-MISSING-SIGNALR follow-up (2026-05-06) =====================
        // Phase-12 follow-up tests covering the SignalR refactor that landed in commit
        // 6c587de3d (MangaMissingController extends RestControllerWithSignalR<ChapterResource,
        // Chapter> after the canonical-resource-reuse follow-up + subscribes to
        // ChapterGrabbedEvent / ChapterImportedEvent / ChapterFileDeletedEvent). Without these
        // tests, a future Phase 8/15 collapse could silently revert the controller to plain
        // Controller and the page-auto-refresh contract would silently break (no React Query
        // updatePagedItem on chapter/file pipeline events). Mirrors MangaCutoffControllerFixture's
        // F-CUTOFF-SIGNALR test block verbatim for cross-controller consistency.
        //
        // Predicate locked per MangaControllerSignalRFixture pattern (W6 revision iteration 1
        // shape extracted verbatim from RestControllerWithSignalR.cs:81-89): SignalRMessage
        // construction confirms Name = Resource = "manga/wanted/missing" + Action =
        // ModelAction.Updated. The "manga/wanted/missing" literal auto-derives from
        // [V5ApiController("manga/wanted/missing")] via RestControllerWithSignalR.cs:23-33.

        [Test]
        public void Controller_extends_RestControllerWithSignalR_per_F_MISSING_SIGNALR_followup()
        {
            // Pin the base-class shape so a future Phase 8/15 collapse cannot silently revert
            // to plain Controller (which would dead-letter the SignalR emission contract). The
            // assertion is on the open generic to avoid coupling to TResource/TModel name
            // changes — the LOAD-bearing fact is "this controller has the SignalR base".
            typeof(MangaMissingController).BaseType.Should().NotBeNull();
            typeof(MangaMissingController).BaseType!.IsGenericType.Should().BeTrue(
                "MangaMissingController must extend a generic SignalR base — F-MISSING-SIGNALR contract");
            typeof(MangaMissingController).BaseType!.GetGenericTypeDefinition()
                .Should().Be(typeof(RestControllerWithSignalR<,>),
                    "MangaMissingController must extend RestControllerWithSignalR<,> so the React " +
                    "Query cache for ['/manga/wanted/missing'] auto-refreshes on chapter/file pipeline events");
        }

        [Test]
        public void Handle_ChapterGrabbedEvent_should_BroadcastResourceChange_Updated_for_each_chapter_in_RemoteChapter()
        {
            // RemoteChapter.Chapters is the resolved Chapter list per
            // src/NzbDrone.Core/Parser/Manga/Model/RemoteChapter.cs:32. The handler iterates
            // per-chapter and broadcasts Updated by id, mirroring TV
            // EpisodeControllerWithSignalR.Handle(EpisodeGrabbedEvent) at lines 122-132 +
            // MangaCutoffController sibling.
            var remoteChapter = new RemoteChapter
            {
                Chapters = new List<NzbDrone.Core.Manga.Chapter>
                {
                    new() { Id = 1, MangaId = 42, ChapterNumber = 1m },
                    new() { Id = 2, MangaId = 42, ChapterNumber = 2m },
                }
            };

            // ChapterGrabbedEvent ctor: (RemoteChapter, string downloadId, string downloadClient) —
            // verified src/NzbDrone.Core/MediaFiles/ChapterArchiving/ChapterGrabbedEvent.cs:17.
            Subject.Handle(new ChapterGrabbedEvent(remoteChapter, "dl-1", "InProcess"));

            // One broadcast per chapter id (Times.Once each). Predicate binds on Resource.Id
            // so a regression that captures only the first id (or always Id=1) fails the assertion.
            // Body type is now ResourceChangeMessage<ChapterResource> (canonical resource reuse).
            foreach (var id in new[] { 1, 2 })
            {
                var expectedId = id;
                Mocker.GetMock<IBroadcastSignalRMessage>()
                      .Verify(b => b.BroadcastMessage(It.Is<SignalRMessage>(m =>
                          m.Action == ModelAction.Updated &&
                          m.Name == "manga/wanted/missing" &&
                          ((ResourceChangeMessage<ChapterResource>)m.Body).Resource.Id == expectedId)),
                          Times.Once);
            }
        }

        [Test]
        public void Handle_ChapterImportedEvent_should_BroadcastResourceChange_Updated_for_imported_chapter_id()
        {
            // ChapterImportedEvent carries a single Chapter (NOT a list) — manga import pipeline
            // imports one chapter per event per Plan 06-07 Pitfall 4 (publish AFTER ChapterFile
            // DB commit + filesystem move complete). Verify
            // src/NzbDrone.Core/MediaFiles/MangaImport/ChapterImportedEvent.cs:14-20.
            var importedEvent = new ChapterImportedEvent
            {
                Manga = new NzbDrone.Core.Manga.Manga { Id = 42, Title = "Test Manga" },
                Chapter = new NzbDrone.Core.Manga.Chapter { Id = 7, MangaId = 42, ChapterNumber = 3m },
                ChapterFile = new ChapterFile { Id = 99, MangaId = 42, ChapterId = 7 },
                NewDownload = true
            };

            Subject.Handle(importedEvent);

            // Single broadcast for the imported chapter's Id.
            Mocker.GetMock<IBroadcastSignalRMessage>()
                  .Verify(b => b.BroadcastMessage(It.Is<SignalRMessage>(m =>
                      m.Action == ModelAction.Updated &&
                      m.Name == "manga/wanted/missing" &&
                      ((ResourceChangeMessage<ChapterResource>)m.Body).Resource.Id == 7)),
                      Times.Once);
        }

        [Test]
        public void Handle_ChapterFileDeletedEvent_should_BroadcastResourceChange_Updated_with_chapter_id()
        {
            // ChapterFileDeletedEvent ctor: (ChapterFile, DeleteMediaFileReason) — verified
            // src/NzbDrone.Core/MediaFiles/Events/ChapterFileDeletedEvent.cs:12. The ChapterFile
            // carries a single ChapterId (verified src/NzbDrone.Core/MediaFiles/ChapterFile.cs:14).
            // DeleteMediaFileReason.Manual bypasses the Upgrade short-circuit so the broadcast fires.
            var chapterFile = Builder<ChapterFile>.CreateNew()
                .With(c => c.Id = 99)
                .With(c => c.MangaId = 42)
                .With(c => c.ChapterId = 7)
                .Build();

            Subject.Handle(new ChapterFileDeletedEvent(chapterFile, DeleteMediaFileReason.Manual));

            // Single broadcast for the deleted file's ChapterId (NOT the file Id).
            Mocker.GetMock<IBroadcastSignalRMessage>()
                  .Verify(b => b.BroadcastMessage(It.Is<SignalRMessage>(m =>
                      m.Action == ModelAction.Updated &&
                      m.Name == "manga/wanted/missing" &&
                      ((ResourceChangeMessage<ChapterResource>)m.Body).Resource.Id == 7)),
                      Times.Once);
        }

        [Test]
        public void Handle_ChapterFileDeletedEvent_should_NOT_broadcast_when_reason_is_Upgrade()
        {
            // Upgrade-reason short-circuit mirrors MangaController.Handle(ChapterFileDeletedEvent)
            // at MangaController.cs:336-341 + MangaCutoffController sibling: the upcoming Add
            // event will fire next, so broadcasting twice for one logical change is wasteful.
            // Verify the short-circuit by asserting BroadcastMessage was NEVER called.
            var chapterFile = Builder<ChapterFile>.CreateNew()
                .With(c => c.Id = 99)
                .With(c => c.MangaId = 42)
                .With(c => c.ChapterId = 7)
                .Build();

            Subject.Handle(new ChapterFileDeletedEvent(chapterFile, DeleteMediaFileReason.Upgrade));

            Mocker.GetMock<IBroadcastSignalRMessage>()
                  .Verify(
                      b => b.BroadcastMessage(It.IsAny<SignalRMessage>()),
                      Times.Never,
                      "Upgrade-reason ChapterFileDeletedEvent must NOT broadcast — the upcoming " +
                      "Add event fires next; mirrors MangaController + MangaCutoffController precedent");
        }
    }
}
