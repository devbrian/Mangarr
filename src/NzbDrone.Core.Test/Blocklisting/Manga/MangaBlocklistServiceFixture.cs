using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Blocklisting.Manga;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Blocklisting.Manga
{
    // Phase 6 D-11 + D-19 — exercises:
    //   - Pitfall 5 mitigation (variants: happy / case / trim / null SourceKey fallback /
    //     null-Guid fallback on seed + empty-Guid fallback on search + both-guids-differ no-overmatch
    //     guard — the null-Guid variants are the debug auto-retry-loop-guid-mismatch regression, 2026-06-13)
    //   - Negative case (no match)
    //   - Handle(ChapterDownloadFailedEvent) populates row from release identity triple
    //   - Insert-before-PublishEvent ordering invariant (mock interaction sequence)
    //   - null-Release tolerance for legacy 4-arg ChapterDownloadFailedEvent emit sites
    //   - HandleAsync(MangaDeletedEvent) cascade
    //   - Execute(ClearMangaBlocklistCommand) purges
    [TestFixture]
    public class MangaBlocklistServiceFixture : CoreTest<MangaBlocklistService>
    {
        private const int MangaId = 7;

        private MangaBlocklist BuildSeed(string sourceTitle = "Vinland Saga - 0001",
                                        string sourceKey = "MangaDex",
                                        string releaseGuid = "g1")
        {
            return new MangaBlocklist
            {
                MangaId = MangaId,
                SourceTitle = sourceTitle,
                SourceKey = sourceKey,
                ReleaseGuid = releaseGuid
            };
        }

        private void SeedRepository(params MangaBlocklist[] rows)
        {
            Mocker.GetMock<IMangaBlocklistRepository>()
                .Setup(r => r.BlocklistedByTitle(MangaId, It.IsAny<string>()))
                .Returns(new List<MangaBlocklist>(rows));
        }

        // ── Pitfall 5 ────────────────────────────────────────────────────────────────────

        [Test]
        public void Blocklisted_returns_true_for_exact_triple_match_happy_path()
        {
            SeedRepository(BuildSeed());

            var release = new ReleaseInfo
            {
                Title = "Vinland Saga - 0001",
                Indexer = "MangaDex",
                Guid = "g1"
            };

            Subject.Blocklisted(MangaId, release).Should().BeTrue();
        }

        [Test]
        public void Blocklisted_returns_true_when_title_case_differs_pitfall5_case()
        {
            SeedRepository(BuildSeed(sourceTitle: "Vinland Saga - 0001"));

            var release = new ReleaseInfo
            {
                Title = "vinland saga - 0001",
                Indexer = "MangaDex",
                Guid = "g1"
            };

            Subject.Blocklisted(MangaId, release).Should().BeTrue();
        }

        [Test]
        public void Blocklisted_returns_true_when_title_has_surrounding_whitespace_pitfall5_trim()
        {
            SeedRepository(BuildSeed(sourceTitle: "Vinland Saga - 0001"));

            var release = new ReleaseInfo
            {
                Title = "  Vinland Saga - 0001  ",
                Indexer = "MangaDex",
                Guid = "g1"
            };

            Subject.Blocklisted(MangaId, release).Should().BeTrue();
        }

        [Test]
        public void Blocklisted_returns_true_when_source_key_null_on_seed_pitfall5_null_fallback()
        {
            // Pitfall 5: legacy / migrated rows may lack a SourceKey value — null-tolerant
            // fallback treats the comparison as match-on-(Title, Guid) only.
            SeedRepository(BuildSeed(sourceKey: null));

            var release = new ReleaseInfo
            {
                Title = "Vinland Saga - 0001",
                Indexer = null,
                Guid = "g1"
            };

            Subject.Blocklisted(MangaId, release).Should().BeTrue();
        }

        [Test]
        public void Blocklisted_returns_true_when_release_guid_null_on_seed_pitfall5_null_guid_fallback()
        {
            // REGRESSION (debug auto-retry-loop-guid-mismatch, 2026-06-13): the failure path
            // (MangaTrackedDownloadService.MapFromHistory) reconstructs RemoteChapter.Release WITHOUT
            // the gateway Guid, so MangaBlocklistService.Handle stores ReleaseGuid = null. The very
            // next re-search returns the same release carrying the gateway's REQUIRED non-empty guid.
            // Before the fix, the strict guid compare ("".Equals("<guid>") == false) never re-matched,
            // so the just-blocklisted release was re-grabbed every ~5s forever. The null-tolerant guid
            // fallback (symmetric with the SourceKey fallback) must treat this as match-on-(Title,
            // SourceKey). NOTE: the pre-existing pitfall5_null_fallback test only varied SourceKey
            // nullity — it did NOT cover Guid nullity, which is the exact gap that shipped this loop.
            SeedRepository(BuildSeed(releaseGuid: null));

            var release = new ReleaseInfo
            {
                Title = "Vinland Saga - 0001",
                Indexer = "MangaDex",
                Guid = "gateway-required-non-empty-guid"
            };

            Subject.Blocklisted(MangaId, release).Should().BeTrue();
        }

        [Test]
        public void Blocklisted_returns_true_when_release_guid_empty_string_on_search_pitfall5_null_guid_fallback()
        {
            // Mirror of the above with the nullity on the SEARCH side (empty-string guid). Blocklisted()
            // normalizes a null search guid to string.Empty, so the empty-side fallback must hold there
            // too — otherwise a re-search whose release lost its guid would never re-match a seed that
            // kept one.
            SeedRepository(BuildSeed(releaseGuid: "g1"));

            var release = new ReleaseInfo
            {
                Title = "Vinland Saga - 0001",
                Indexer = "MangaDex",
                Guid = string.Empty
            };

            Subject.Blocklisted(MangaId, release).Should().BeTrue();
        }

        [Test]
        public void Blocklisted_returns_false_when_both_guids_present_but_differ_no_overmatch()
        {
            // Guard the null-tolerant fallback does NOT over-match: when BOTH sides carry a non-empty
            // guid and they differ, the releases are genuinely distinct and must NOT collide even though
            // Title + SourceKey are identical. The fallback only relaxes when a guid is absent.
            SeedRepository(BuildSeed(sourceTitle: "Vinland Saga - 0001", sourceKey: "MangaDex", releaseGuid: "g1"));

            var release = new ReleaseInfo
            {
                Title = "Vinland Saga - 0001",
                Indexer = "MangaDex",
                Guid = "g2"
            };

            Subject.Blocklisted(MangaId, release).Should().BeFalse();
        }

        [Test]
        public void Blocklisted_returns_false_for_completely_different_release()
        {
            SeedRepository(BuildSeed());

            var release = new ReleaseInfo
            {
                Title = "Berserk - 0099",
                Indexer = "MangaDex",
                Guid = "completely-different-guid"
            };

            Subject.Blocklisted(MangaId, release).Should().BeFalse();
        }

        [Test]
        public void Blocklisted_returns_false_when_release_is_null()
        {
            Subject.Blocklisted(MangaId, null).Should().BeFalse();
        }

        // ── Handle(ChapterDownloadFailedEvent) ───────────────────────────────────────────

        [Test]
        public void Handle_ChapterDownloadFailedEvent_inserts_blocklist_row_with_release_identity_triple()
        {
            var release = new ReleaseInfo
            {
                Title = "Vinland Saga - 0001",
                Indexer = "MangaDex",
                Guid = "g1"
            };
            var failedEvent = new ChapterDownloadFailedEvent(rowId: 5, mangaId: 7, chapterId: 42, failureReason: "404 image missing")
            {
                SourceTitle = "Vinland Saga - 0001",
                Source = "ImageDownload",
                DownloadClient = "InProcess",
                Release = release
            };

            Subject.Handle(failedEvent);

            Mocker.GetMock<IMangaBlocklistRepository>().Verify(r => r.Insert(It.Is<MangaBlocklist>(b =>
                b.MangaId == 7 &&
                b.ChapterIds.Count == 1 && b.ChapterIds[0] == 42 &&
                b.SourceTitle == "Vinland Saga - 0001" &&
                b.SourceKey == "MangaDex" &&
                b.ReleaseGuid == "g1" &&
                b.Reason == "404 image missing" &&
                b.Source == "ImageDownload" &&
                b.ReleaseInfoJson != null)));
        }

        [Test]
        public void Handle_ChapterDownloadFailedEvent_publishes_added_event_AFTER_insert_ordering_invariant()
        {
            // ORDERING TEST: Insert must run BEFORE PublishEvent so subscribers (Plan 06-08
            // AutoRetryOrchestrator) see the row when they query the repository in response.
            var release = new ReleaseInfo { Title = "X", Indexer = "MangaDex", Guid = "g1" };
            var failedEvent = new ChapterDownloadFailedEvent(rowId: 5, mangaId: 7, chapterId: 42, failureReason: "fail")
            {
                Release = release
            };

            // Sequence tracking: append a token to the list as each call lands. Then assert order.
            var sequence = new List<string>();
            Mocker.GetMock<IMangaBlocklistRepository>()
                .Setup(r => r.Insert(It.IsAny<MangaBlocklist>()))
                .Callback<MangaBlocklist>(_ => sequence.Add("Insert"))
                .Returns<MangaBlocklist>(b => b);
            Mocker.GetMock<IEventAggregator>()
                .Setup(e => e.PublishEvent(It.IsAny<MangaBlocklistAddedEvent>()))
                .Callback<MangaBlocklistAddedEvent>(_ => sequence.Add("Publish"));

            Subject.Handle(failedEvent);

            sequence.Should().ContainInOrder("Insert", "Publish");
            sequence[0].Should().Be("Insert", "Insert must run before PublishEvent so AutoRetryOrchestrator sees the committed row");
            sequence[1].Should().Be("Publish");
        }

        [Test]
        public void Handle_ChapterDownloadFailedEvent_with_null_Release_falls_back_to_message_text()
        {
            // Legacy 4-arg ChapterDownloadFailedEvent emit sites: Release/SourceTitle/DownloadClient/
            // Source are null. Service must tolerate; SourceTitle falls back to event.Message.
            var failedEvent = new ChapterDownloadFailedEvent(rowId: 5, mangaId: 7, chapterId: 42, failureReason: "legacy fallback");

            Subject.Handle(failedEvent);

            Mocker.GetMock<IMangaBlocklistRepository>().Verify(r => r.Insert(It.Is<MangaBlocklist>(b =>
                b.MangaId == 7 &&
                b.SourceTitle == "legacy fallback" &&
                b.SourceKey == null &&
                b.ReleaseGuid == null &&
                b.ReleaseInfoJson == null)));
        }

        // ── HandleAsync(MangaDeletedEvent) ───────────────────────────────────────────────

        [Test]
        public void HandleAsync_MangaDeletedEvent_cascades_delete_for_manga()
        {
            var manga = new NzbDrone.Core.Manga.Manga { Id = 7 };
            var deleted = new MangaDeletedEvent(manga, deleteFiles: true);

            Subject.HandleAsync(deleted);

            Mocker.GetMock<IMangaBlocklistRepository>().Verify(r => r.DeleteForManga(7), Times.Once);
        }

        // ── Execute(ClearMangaBlocklistCommand) ──────────────────────────────────────────

        [Test]
        public void Execute_ClearMangaBlocklistCommand_purges_repository()
        {
            Subject.Execute(new ClearMangaBlocklistCommand());

            Mocker.GetMock<IMangaBlocklistRepository>().Verify(r => r.Purge(false), Times.Once);
        }

        // ── Block(...) (manual UI insert path) ───────────────────────────────────────────

        [Test]
        public void Block_inserts_row_then_publishes_added_event()
        {
            var sequence = new List<string>();
            Mocker.GetMock<IMangaBlocklistRepository>()
                .Setup(r => r.Insert(It.IsAny<MangaBlocklist>()))
                .Callback<MangaBlocklist>(_ => sequence.Add("Insert"))
                .Returns<MangaBlocklist>(b => b);
            Mocker.GetMock<IEventAggregator>()
                .Setup(e => e.PublishEvent(It.IsAny<MangaBlocklistAddedEvent>()))
                .Callback<MangaBlocklistAddedEvent>(_ => sequence.Add("Publish"));

            Subject.Block(new MangaBlocklist { MangaId = 7, SourceTitle = "Manual entry" });

            sequence.Should().ContainInOrder("Insert", "Publish");
        }
    }
}
