#nullable enable
using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Mangarr.Api.V5.Manga;
using Microsoft.AspNetCore.Mvc.Routing;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Manga;
using NzbDrone.SignalR;
using NzbDrone.Test.Common;

namespace NzbDrone.Api.Test.Manga
{
    // Issue #28 (2026-05-09) — pins the MangaController.UpdateManga round-trip for the
    // three fields PR #27 deliberately deferred from the single-Manga Edit modal:
    // MonitorNewItems, TranslationProfileId, CustomFormatProfileId.
    //
    // Background: PR #27 shipped the single-Manga Edit modal scoped to Monitored + Tags
    // because three fields on the core Manga model could not yet round-trip end-to-end
    // through the V5 controller stack:
    //   * MonitorNewItems was not on the Manga core model OR the resource.
    //   * TranslationProfileId was on the Manga core model since Phase 5 D-01 but was
    //     not exposed on MangaResource and was not copied by Manga.ApplyChanges.
    //   * CustomFormatProfileId was on the Manga core model since Phase 5 D-07 but was
    //     not exposed on MangaResource and was not copied by Manga.ApplyChanges.
    //
    // The Issue #28 fix (this PR) adds:
    //   * `enum MangaMonitorNewItems { All, None }` + Manga.MonitorNewItems property +
    //     migration 001 column (edit-001-in-place per dev-migration-policy.md).
    //   * MangaResource.{MonitorNewItems, TranslationProfileId, CustomFormatProfileId}
    //     wire fields + ToResource / ToModel mapper round-trip.
    //   * Three new copies in Manga.ApplyChanges (alongside the existing Monitored /
    //     Tags / RootFolderPath copies from Phase 8 audit gap-02).
    //
    // The MangaController.UpdateManga PUT path (MangaController.cs:153-175) calls
    //   existing.ApplyChanges(resource.ToModel()!);
    //   _mangaService.UpdateManga(existing, publishUpdatedEvent: true, triggerSeriesEdited: true);
    //
    // These tests verify every field arrives at the IMangaService.UpdateManga call site
    // with the value the inbound resource carried — guarding against regressions that
    // re-drop any of the three new fields from the resource, the mapper, or ApplyChanges.
    //
    // Issue #96 (2026-05-12) — extended the fixture with the Path-preservation regression
    // pin (tests 6 + 7 below). External API callers that PUT a partial body without
    // `path` previously clobbered existing.Path in memory via Manga.ApplyChanges (issue
    // #81 unconditional `Path = other.Path` copy at Manga.cs:135), then tripped the
    // SQLite NOT NULL constraint on persist. The MangaController.UpdateManga fix mirrors
    // PR #95's RefreshMangaService save/restore pattern (MangaController.cs:192-194).
    //
    // Per-fixture unit-test filter:
    //   dotnet test --filter "FullyQualifiedName~MangaControllerUpdateMangaFixture"
    //
    // Tests (7):
    //   1. UpdateManga_round_trips_MonitorNewItems_All
    //   2. UpdateManga_round_trips_MonitorNewItems_None
    //   3. UpdateManga_round_trips_TranslationProfileId
    //   4. UpdateManga_round_trips_CustomFormatProfileId
    //   5. UpdateManga_round_trips_all_three_fields_together
    //   6. UpdateManga_preserves_existing_Path_when_resource_omits_path (issue #96)
    //   7. UpdateManga_overwrites_existing_Path_when_resource_supplies_path (issue #96)
    [TestFixture]
    public class MangaControllerUpdateMangaFixture : TestBase<MangaController>
    {
        private NzbDrone.Core.Manga.Manga _existing = null!;
        private NzbDrone.Core.Manga.Manga? _captured;

        [SetUp]
        public void Setup()
        {
            // Build an "existing" manga the controller will fetch via GetManga(id).
            // ApplyChanges mutates this instance in place; the controller then hands
            // the mutated instance to IMangaService.UpdateManga. We capture it via the
            // 3-arg overload (Plan 10-07 Option B locked path; controller line 172).
            _existing = Builder<NzbDrone.Core.Manga.Manga>.CreateNew()
                .With(m => m.Id = 17)
                .With(m => m.Title = "Existing Manga")
                .With(m => m.Path = "C:\\Manga\\Existing")
                .With(m => m.Monitored = true)
                .With(m => m.MonitorNewItems = MangaMonitorNewItems.All)
                .With(m => m.TranslationProfileId = 1)
                .With(m => m.CustomFormatProfileId = 1)
                .With(m => m.Tags = new HashSet<int>())
                .With(m => m.Images = new List<NzbDrone.Core.MediaCover.MediaCover>())
                .With(m => m.Genres = new List<string>())
                .Build();

            Mocker.GetMock<IMangaService>()
                  .Setup(s => s.GetManga(17))
                  .Returns(() => _existing);

            _captured = null;
            Mocker.GetMock<IMangaService>()
                  .Setup(s => s.UpdateManga(It.IsAny<NzbDrone.Core.Manga.Manga>(), It.IsAny<bool>(), It.IsAny<bool>()))
                  .Callback<NzbDrone.Core.Manga.Manga, bool, bool>((m, _, _) => _captured = m)
                  .Returns<NzbDrone.Core.Manga.Manga, bool, bool>((m, _, _) => m);

            // MapResource → ComputeStatistics path; keep NRE-free.
            Mocker.GetMock<IChapterService>()
                  .Setup(s => s.GetChaptersByManga(It.IsAny<int>()))
                  .Returns(new List<NzbDrone.Core.Manga.Chapter>());

            Mocker.GetMock<IBroadcastSignalRMessage>()
                  .SetupGet(b => b.IsConnected)
                  .Returns(false);

            // The TypedAccepted path hits Url.Action — stub it so the call site succeeds.
            var urlHelper = new Mock<Microsoft.AspNetCore.Mvc.IUrlHelper>();
            urlHelper.Setup(u => u.Action(It.IsAny<UrlActionContext>())).Returns("/api/v5/manga/17");
            Subject.Url = urlHelper.Object;
        }

        private static MangaResource BuildResource(
            MangaMonitorNewItems monitorNewItems = MangaMonitorNewItems.All,
            int? translationProfileId = 1,
            int? customFormatProfileId = 1,
            string? path = "C:\\Manga\\Existing")
        {
            return Builder<MangaResource>.CreateNew()
                .With(r => r.Id = 17)
                .With(r => r.Title = "Existing Manga")
                .With(r => r.Path = path!)
                .With(r => r.Monitored = true)
                .With(r => r.MonitorNewItems = monitorNewItems)
                .With(r => r.TranslationProfileId = translationProfileId)
                .With(r => r.CustomFormatProfileId = customFormatProfileId)
                .With(r => r.Tags = new HashSet<int>())
                .Build();
        }

        [Test]
        public void UpdateManga_round_trips_MonitorNewItems_All()
        {
            // Pre-condition: existing row already carries All (the seed default).
            // Send an explicit All to verify the round-trip pins the wire-shape.
            var resource = BuildResource(monitorNewItems: MangaMonitorNewItems.All);

            Subject.UpdateManga(resource);

            _captured.Should().NotBeNull("UpdateManga must be invoked with the mutated existing instance");
            _captured!.MonitorNewItems.Should().Be(MangaMonitorNewItems.All,
                "ApplyChanges must copy MonitorNewItems = All from the resource");
        }

        [Test]
        public void UpdateManga_round_trips_MonitorNewItems_None()
        {
            // Inverse of test 1 — flip the existing All to None and verify the change
            // arrives at the service layer. Guards against a regression that drops the
            // MonitorNewItems copy from Manga.ApplyChanges (which would silently keep
            // the old value).
            var resource = BuildResource(monitorNewItems: MangaMonitorNewItems.None);

            Subject.UpdateManga(resource);

            _captured!.MonitorNewItems.Should().Be(MangaMonitorNewItems.None,
                "ApplyChanges must overwrite MonitorNewItems with None when the user picks None");
        }

        [Test]
        public void UpdateManga_round_trips_TranslationProfileId()
        {
            // Existing row carries TranslationProfileId=1. Send 7 — verify ApplyChanges
            // overwrites the existing value rather than preserving it.
            var resource = BuildResource(translationProfileId: 7);

            Subject.UpdateManga(resource);

            _captured!.TranslationProfileId.Should().Be(7,
                "ApplyChanges must copy TranslationProfileId from the resource (was previously dropped pre-#28)");
        }

        [Test]
        public void UpdateManga_round_trips_CustomFormatProfileId()
        {
            // Same shape as test 3 for the second profile FK.
            var resource = BuildResource(customFormatProfileId: 9);

            Subject.UpdateManga(resource);

            _captured!.CustomFormatProfileId.Should().Be(9,
                "ApplyChanges must copy CustomFormatProfileId from the resource (was previously dropped pre-#28)");
        }

        [Test]
        public void UpdateManga_round_trips_all_three_fields_together()
        {
            // Canonical UI path: user changes all three fields, hits Save. Guards against
            // a regression that drops one of the three copies while keeping the others.
            var resource = BuildResource(
                monitorNewItems: MangaMonitorNewItems.None,
                translationProfileId: 11,
                customFormatProfileId: 13);

            Subject.UpdateManga(resource);

            _captured!.MonitorNewItems.Should().Be(MangaMonitorNewItems.None);
            _captured!.TranslationProfileId.Should().Be(11);
            _captured!.CustomFormatProfileId.Should().Be(13);

            Mocker.GetMock<IMangaService>()
                  .Verify(s => s.UpdateManga(
                              It.Is<NzbDrone.Core.Manga.Manga>(m =>
                                  m.MonitorNewItems == MangaMonitorNewItems.None &&
                                  m.TranslationProfileId == 11 &&
                                  m.CustomFormatProfileId == 13),
                              true,  // publishUpdatedEvent — Plan 10-07 controller-PUT contract
                              true), // triggerSeriesEdited — Plan 10-07 controller-PUT contract
                          Times.Once,
                          "UpdateManga must be called exactly once with all three Issue #28 fields populated " +
                          "and the Plan 10-07 dual-publish flags both true");
        }

        [Test]
        public void UpdateManga_preserves_existing_Path_when_resource_omits_path()
        {
            // Issue #96 regression pin (2026-05-12). External API callers (curl, scripts,
            // third-party integrations) frequently PUT partial bodies that omit `path`.
            // Pre-fix: ToModel produced a model with Path=null, Manga.ApplyChanges
            // (Manga.cs:135) unconditionally copied `Path = other.Path` and clobbered
            // existing.Path in memory; the trailing _mangaService.UpdateManga then tripped
            // the SQLite NOT NULL constraint on Manga.Path. Post-fix: MangaController.cs
            // saves userPath = existing.Path BEFORE ApplyChanges and restores it via
            // `existing.Path = !string.IsNullOrWhiteSpace(existing.Path) ? existing.Path : userPath`
            // AFTER. This test pins the post-fix behavior: when path is omitted, the
            // existing on-disk path survives the round-trip.
            var resource = BuildResource(path: null);

            Subject.UpdateManga(resource);

            _captured.Should().NotBeNull("UpdateManga must be invoked even when resource omits path");
            _captured!.Path.Should().Be("C:\\Manga\\Existing",
                "issue #96: when resource.Path is null/empty, MangaController must restore " +
                "existing.Path from userPath rather than letting ApplyChanges clobber it to null");
        }

        [Test]
        public void UpdateManga_overwrites_existing_Path_when_resource_supplies_path()
        {
            // Issue #96 companion test (2026-05-12). The save/restore guard must be a
            // no-op when the caller supplies a non-empty Path — i.e., the frontend's
            // normal Save path (MangaResourceMapper.ToModel always populates Path) and
            // the issue #81 MoveMangaCommand wire-up (which reads resource.Path BEFORE
            // ApplyChanges) must continue to work. This test pins that contract: when
            // the resource carries a different Path, ApplyChanges overwrites existing.Path
            // and the restore branch does NOT fire.
            var resource = BuildResource(path: "C:\\Manga\\Renamed");

            Subject.UpdateManga(resource);

            _captured.Should().NotBeNull();
            _captured!.Path.Should().Be("C:\\Manga\\Renamed",
                "issue #96 fix must be a no-op when resource supplies a non-empty Path — " +
                "ApplyChanges overwrites with the new path (preserves frontend Save + issue-#81 MoveManga semantics)");
        }
    }
}
