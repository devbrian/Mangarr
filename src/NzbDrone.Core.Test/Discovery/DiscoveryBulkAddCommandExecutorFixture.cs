using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Discovery;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Discovery
{
    // Phase 42 Plan 42-03 (DISC-07) — DiscoveryBulkAddCommandExecutor.Execute behavior.
    //
    // Analog: ImportListTests/ImportListSyncServiceFixture.cs (Moq IAddMangaService) +
    // Messaging/Commands/CommandExecutorFixture.cs. No live HTTP / no live AddManga pipeline —
    // IAddMangaService is Moq-stubbed; the focus is the executor's fan-out to the bulk
    // AddManga(List<Manga>, true) path + the failure-isolation (no-abort) property.
    [TestFixture]
    public class DiscoveryBulkAddCommandExecutorFixture : CoreTest<DiscoveryBulkAddCommandExecutor>
    {
        private DiscoveryBulkAddCommand BuildCommand(params int[] ids)
        {
            return new DiscoveryBulkAddCommand
            {
                MangaBakaIds = ids.ToList(),
                RootFolderPath = @"C:\Manga",
                Monitor = MangaMonitor.All,
                TranslationProfileId = 1,
                CustomFormatProfileId = 1,
                Tags = new List<int> { 7 },
                SearchForMissingChapters = true
            };
        }

        [Test]
        public void should_add_one_manga_per_mangabaka_id_via_bulk_path()
        {
            Subject.Execute(BuildCommand(101, 102, 103));

            Mocker.GetMock<IAddMangaService>()
                  .Verify(s => s.AddManga(
                      It.Is<List<Manga.Manga>>(l => l.Count == 3 && l.All(m => m.MangaBakaId != null)),
                      true),
                      Times.Once());
        }

        [Test]
        public void should_carry_add_options_from_command_onto_each_manga()
        {
            Subject.Execute(BuildCommand(101, 102, 103));

            Mocker.GetMock<IAddMangaService>()
                  .Verify(s => s.AddManga(
                      It.Is<List<Manga.Manga>>(l => l.All(m =>
                          m.Monitored &&
                          m.RootFolderPath == @"C:\Manga" &&
                          m.TranslationProfileId == 1 &&
                          m.CustomFormatProfileId == 1 &&
                          m.Tags.Contains(7) &&
                          m.AddOptions != null &&
                          m.AddOptions.Monitor == MangaMonitor.All &&
                          m.AddOptions.SearchForMissingChapters)),
                      true),
                      Times.Once());
        }

        [Test]
        public void should_not_propagate_when_add_manga_throws()
        {
            Mocker.GetMock<IAddMangaService>()
                  .Setup(s => s.AddManga(It.IsAny<List<Manga.Manga>>(), It.IsAny<bool>()))
                  .Throws(new InvalidOperationException("boom"));

            Action exec = () => Subject.Execute(BuildCommand(101, 102, 103));

            exec.Should().NotThrow();
        }
    }
}
