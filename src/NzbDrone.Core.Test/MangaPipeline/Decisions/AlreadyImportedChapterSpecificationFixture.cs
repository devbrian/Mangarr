using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Manga.Specifications;
using NzbDrone.Core.History.Manga;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MangaPipeline.Decisions
{
    // quick-260615 — faithful Sonarr port of the grab-decision AlreadyImported spec.
    // Replaces the Phase 6 STUB fixture (which only asserted "any Imported row → reject").
    //
    // The spec now mirrors TV AlreadyImportedSpecification: it rejects ONLY the same release
    // that was grabbed AND imported, and ONLY while the chapter still has a current file. The
    // headline regression this guards is delete→redownload: a chapter whose ChapterFile was
    // deleted (ChapterFileId == null) must NOT be blocked by its stale Imported history row.
    //
    // BL-01 cross-domain ID-collision guard is preserved structurally: the spec depends on
    // IChapterHistoryService (ChapterHistory table) — it can never reach EpisodeHistory.
    //
    // Pitfall 6 GUARD note: the class still implements IMangaDecisionEngineSpecification only,
    // so the 11-spec auto-discovery count (MangaDownloadDecisionMakerEndToEndFixture) is stable.
    [TestFixture]
    public class AlreadyImportedChapterSpecificationFixture : DbTest
    {
        private const string ReleaseGuid = "comix:test-manga:1";
        private const string ReleaseTitle = "Test Manga - Chapter 001";
        private const string DownloadId = "download-abc";

        private AlreadyImportedChapterSpecification _spec;
        private Mock<IChapterHistoryService> _chapterHistoryService;

        [SetUp]
        public void Setup()
        {
            _chapterHistoryService = new Mock<IChapterHistoryService>();
            _spec = new AlreadyImportedChapterSpecification(_chapterHistoryService.Object, LogManager.GetLogger("test"));
        }

        // hasFile defaults to true: most cases exercise a chapter that currently has a file.
        private RemoteChapter BuildRemoteChapter(int chapterId, bool hasFile = true, string releaseGuid = ReleaseGuid, string releaseTitle = ReleaseTitle)
        {
            return new RemoteChapter
            {
                Manga = new NzbDrone.Core.Manga.Manga { Id = 7, Title = "Test Manga" },
                Chapters = new List<Chapter>
                {
                    new()
                    {
                        Id = chapterId,
                        MangaId = 7,
                        Monitored = true,
                        ChapterNumber = 1m,
                        ChapterFileId = hasFile ? 999 : null
                    }
                },
                Release = new ReleaseInfo { Guid = releaseGuid, Title = releaseTitle, Indexer = "Mangarr Gateway" }
            };
        }

        private void SeedHistory(int chapterId, params ChapterHistory[] rows)
        {
            _chapterHistoryService.Setup(s => s.FindByChapterId(chapterId)).Returns(new List<ChapterHistory>(rows));
        }

        private static ChapterHistory Grabbed(int chapterId, DateTime date, string guid = ReleaseGuid, string title = ReleaseTitle, string downloadId = DownloadId)
            => new() { ChapterId = chapterId, MangaId = 7, EventType = ChapterHistoryEventType.Grabbed, Date = date, ReleaseGuid = guid, SourceTitle = title, DownloadId = downloadId };

        private static ChapterHistory Imported(int chapterId, DateTime date, string downloadId = DownloadId)
            => new() { ChapterId = chapterId, MangaId = 7, EventType = ChapterHistoryEventType.Imported, Date = date, DownloadId = downloadId };

        [Test]
        public void Rejects_same_release_when_grabbed_and_imported_and_file_present()
        {
            var grab = new DateTime(2026, 6, 1);
            SeedHistory(50, Imported(50, grab.AddMinutes(10)), Grabbed(50, grab));

            var decision = _spec.IsSatisfiedBy(BuildRemoteChapter(50), new ReleaseDecisionInformation());

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(DownloadRejectionReason.ChapterAlreadyImported);
        }

        [Test]
        public void Rejects_same_release_by_title_when_candidate_has_no_guid()
        {
            // Candidate has no guid (older indexers) so guid matching is unavailable; the title
            // matches the grabbed+imported release — TV-style SourceTitle fallback still rejects.
            var grab = new DateTime(2026, 6, 1);
            SeedHistory(51, Imported(51, grab.AddMinutes(10)), Grabbed(51, grab, guid: "comix:test-manga:1"));

            var subject = BuildRemoteChapter(51, releaseGuid: "", releaseTitle: ReleaseTitle);
            var decision = _spec.IsSatisfiedBy(subject, new ReleaseDecisionInformation());

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(DownloadRejectionReason.ChapterAlreadyImported);
        }

        [Test]
        public void Accepts_when_guids_differ_even_if_titles_match()
        {
            // Both sides have a usable guid but they DIFFER — definitively a different release.
            // A coincidental SourceTitle match must NOT trigger the title fallback (CR #367).
            var grab = new DateTime(2026, 6, 1);
            SeedHistory(56, Imported(56, grab.AddMinutes(10)), Grabbed(56, grab, guid: "comix:test-manga:1", title: ReleaseTitle));

            var subject = BuildRemoteChapter(56, releaseGuid: "mangadex:test-manga:1", releaseTitle: ReleaseTitle);
            var decision = _spec.IsSatisfiedBy(subject, new ReleaseDecisionInformation());

            decision.Accepted.Should().BeTrue("distinct guids mean a different release; title must not be used as a fallback");
        }

        [Test]
        public void Accepts_when_grabbed_download_id_is_empty()
        {
            // A null/empty DownloadId is not a real session id. Grab + import both lacking one must
            // not be paired (null == null) into a false "already imported" rejection (CR #367).
            var grab = new DateTime(2026, 6, 1);
            SeedHistory(57,
                Imported(57, grab.AddMinutes(10), downloadId: null),
                Grabbed(57, grab, downloadId: null));

            var decision = _spec.IsSatisfiedBy(BuildRemoteChapter(57), new ReleaseDecisionInformation());

            decision.Accepted.Should().BeTrue("history rows without a real DownloadId must not be paired");
        }

        [Test]
        public void Accepts_when_chapter_has_no_current_file()
        {
            // THE BUG FIX: file was deleted to force a redownload. The Imported+Grabbed history
            // for the same release still exists, but the chapter has no ChapterFile — it must be
            // grabbable again.
            var grab = new DateTime(2026, 6, 1);
            SeedHistory(52, Imported(52, grab.AddMinutes(10)), Grabbed(52, grab));

            var decision = _spec.IsSatisfiedBy(BuildRemoteChapter(52, hasFile: false), new ReleaseDecisionInformation());

            decision.Accepted.Should().BeTrue("a chapter without a current file cannot be 'already imported'");
        }

        [Test]
        public void Accepts_different_release_even_when_a_release_was_already_imported()
        {
            // Gap 2: a DIFFERENT/better scan (different guid AND title) must remain grabbable.
            var grab = new DateTime(2026, 6, 1);
            SeedHistory(53, Imported(53, grab.AddMinutes(10)), Grabbed(53, grab, guid: "comix:test-manga:1", title: "Test Manga - Chapter 001 [Old Group]"));

            var subject = BuildRemoteChapter(53, releaseGuid: "mangadex:test-manga:1", releaseTitle: "Test Manga - Chapter 001 [Better Group]");
            var decision = _spec.IsSatisfiedBy(subject, new ReleaseDecisionInformation());

            decision.Accepted.Should().BeTrue("a different release than the one already imported must still be grabbable");
        }

        [Test]
        public void Accepts_when_grabbed_but_not_yet_imported()
        {
            // Gap 3: grab exists but no matching Imported row for that DownloadId.
            SeedHistory(54, Grabbed(54, new DateTime(2026, 6, 1)));

            var decision = _spec.IsSatisfiedBy(BuildRemoteChapter(54), new ReleaseDecisionInformation());

            decision.Accepted.Should().BeTrue();
        }

        [Test]
        public void Accepts_when_imported_under_a_different_download_id()
        {
            // The most recent grab was never imported (its DownloadId has no Imported row); an
            // older unrelated import under a different DownloadId must not block.
            var grab = new DateTime(2026, 6, 2);
            SeedHistory(55,
                Grabbed(55, grab, downloadId: "download-new"),
                Imported(55, grab.AddDays(-5), downloadId: "download-old"));

            var decision = _spec.IsSatisfiedBy(BuildRemoteChapter(55), new ReleaseDecisionInformation());

            decision.Accepted.Should().BeTrue();
        }

        [Test]
        public void Accepts_when_no_history()
        {
            SeedHistory(60);

            var decision = _spec.IsSatisfiedBy(BuildRemoteChapter(60), new ReleaseDecisionInformation());

            decision.Accepted.Should().BeTrue();
        }
    }
}
