using System.Collections.Generic;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.IndexerSearch.Manga;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MangaTests
{
    // Phase 32 Plan 32-01 fixture (CORR-01 D-03): MangaScannedHandler.HandleScanEvents
    // 3-branch dispatch matrix.
    //
    //   1. SearchForCutoffUnmetChapters only      => CutoffUnmetChapterSearchCommand (new in CORR-01)
    //   2. SearchForMissingChapters only          => MissingChapterSearchCommand     (unchanged)
    //   3. BOTH flags                             => single bulk MangaSearchCommand  (preserved shortcut)
    //
    // Mirrors the verify-call idiom used at MissingChapterSearchServiceFixture for the
    // IManageCommandQueue.Push 3-arg overload (Push<TCommand>(TCommand, CommandPriority,
    // CommandTrigger) — defaults supplied at the call site via optional args).
    [TestFixture]
    public class MangaScannedHandlerFixture : CoreTest<MangaScannedHandler>
    {
        private NzbDrone.Core.Manga.Manga _manga;

        [SetUp]
        public void Setup()
        {
            _manga = Builder<NzbDrone.Core.Manga.Manga>.CreateNew()
                .With(m => m.Id = 42)
                .With(m => m.Title = "Test Manga")
                .With(m => m.AddOptions = new AddMangaOptions
                {
                    SearchForMissingChapters = false,
                    SearchForCutoffUnmetChapters = false
                })
                .Build();

            // No-op stubs for collaborators invoked before the dispatch branches.
            Mocker.GetMock<IChapterRefreshedService>()
                .Setup(s => s.Search(It.IsAny<int>()));

            Mocker.GetMock<IChapterMonitoredService>()
                .Setup(s => s.SetChapterMonitoredStatus(It.IsAny<NzbDrone.Core.Manga.Manga>(), It.IsAny<AddMangaOptions>()));

            // Post-dispatch RemoveAddOptions sibling — needs a no-op for AutoMoq construction.
            Mocker.GetMock<IMangaService>()
                .Setup(s => s.RemoveAddOptions(It.IsAny<NzbDrone.Core.Manga.Manga>()));
        }

        [Test]
        public void should_push_CutoffUnmetChapterSearchCommand_when_cutoff_only_flag_set()
        {
            _manga.AddOptions.SearchForMissingChapters = false;
            _manga.AddOptions.SearchForCutoffUnmetChapters = true;

            Subject.Handle(new MangaScannedEvent(_manga, new List<string>()));

            // Cutoff-only branch: one CutoffUnmetChapterSearchCommand push with MangaId == 42.
            Mocker.GetMock<IManageCommandQueue>()
                .Verify(q => q.Push(
                        It.Is<CutoffUnmetChapterSearchCommand>(c => c.MangaId == 42),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Once);

            // No bulk MangaSearchCommand fan-out.
            Mocker.GetMock<IManageCommandQueue>()
                .Verify(q => q.Push(
                        It.IsAny<MangaSearchCommand>(),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Never);

            // No missing-only dispatch.
            Mocker.GetMock<IManageCommandQueue>()
                .Verify(q => q.Push(
                        It.IsAny<MissingChapterSearchCommand>(),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Never);
        }

        [Test]
        public void should_push_MissingChapterSearchCommand_when_missing_only_flag_set()
        {
            _manga.AddOptions.SearchForMissingChapters = true;
            _manga.AddOptions.SearchForCutoffUnmetChapters = false;

            Subject.Handle(new MangaScannedEvent(_manga, new List<string>()));

            // Missing-only branch: one MissingChapterSearchCommand push with MangaId == 42.
            Mocker.GetMock<IManageCommandQueue>()
                .Verify(q => q.Push(
                        It.Is<MissingChapterSearchCommand>(c => c.MangaId == 42),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Once);

            // No cutoff-only dispatch.
            Mocker.GetMock<IManageCommandQueue>()
                .Verify(q => q.Push(
                        It.IsAny<CutoffUnmetChapterSearchCommand>(),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Never);

            // No bulk MangaSearchCommand fan-out.
            Mocker.GetMock<IManageCommandQueue>()
                .Verify(q => q.Push(
                        It.IsAny<MangaSearchCommand>(),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Never);
        }

        [Test]
        public void should_push_single_bulk_MangaSearchCommand_when_both_flags_set()
        {
            _manga.AddOptions.SearchForMissingChapters = true;
            _manga.AddOptions.SearchForCutoffUnmetChapters = true;

            Subject.Handle(new MangaScannedEvent(_manga, new List<string>()));

            // Combined-flags shortcut: one bulk MangaSearchCommand carrying the single MangaId.
            Mocker.GetMock<IManageCommandQueue>()
                .Verify(q => q.Push(
                        It.Is<MangaSearchCommand>(c => c.MangaIds != null && c.MangaIds.Contains(42)),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Once);

            // No narrow cutoff-only dispatch.
            Mocker.GetMock<IManageCommandQueue>()
                .Verify(q => q.Push(
                        It.IsAny<CutoffUnmetChapterSearchCommand>(),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Never);

            // No missing-only dispatch.
            Mocker.GetMock<IManageCommandQueue>()
                .Verify(q => q.Push(
                        It.IsAny<MissingChapterSearchCommand>(),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Never);
        }
    }
}
