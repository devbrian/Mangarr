using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.IndexerSearch.Manga;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerSearchTests.Manga
{
    // Phase 6 Plan 06-06 Task 1 — POCO contract tests for the four manga search commands
    // and the IMangaSearchForReleases interface.
    [TestFixture]
    public class MangaSearchCommandsFixture : CoreTest
    {
        [Test]
        public void MangaSearchCommand_carries_MangaIds_and_user_invoked_flag_and_pushes_signalr_updates()
        {
            var cmd = new MangaSearchCommand(new List<int> { 1, 2, 3 }, userInvoked: true);

            cmd.Should().BeAssignableTo<Command>();
            cmd.MangaIds.Should().BeEquivalentTo(new[] { 1, 2, 3 });
            cmd.UserInvokedSearch.Should().BeTrue();
            cmd.SendUpdatesToClient.Should().BeTrue();
        }

        [Test]
        public void MangaSearchCommand_default_ctor_leaves_user_invoked_false()
        {
            var cmd = new MangaSearchCommand();

            cmd.MangaIds.Should().BeNull();
            cmd.UserInvokedSearch.Should().BeFalse();
        }

        [Test]
        public void ChapterSearchCommand_carries_ChapterIds_and_pushes_signalr_updates()
        {
            var cmd = new ChapterSearchCommand(new List<int> { 100, 101 });

            cmd.Should().BeAssignableTo<Command>();
            cmd.ChapterIds.Should().BeEquivalentTo(new[] { 100, 101 });
            cmd.SendUpdatesToClient.Should().BeTrue();
        }

        [Test]
        public void MissingChapterSearchCommand_defaults_monitored_true_and_pushes_signalr_updates()
        {
            var cmd = new MissingChapterSearchCommand();

            cmd.Should().BeAssignableTo<Command>();
            cmd.MangaId.Should().BeNull();
            cmd.Monitored.Should().BeTrue();
            cmd.SendUpdatesToClient.Should().BeTrue();
        }

        [Test]
        public void MissingChapterSearchCommand_with_manga_id_sets_scope_to_single_manga()
        {
            var cmd = new MissingChapterSearchCommand(42);

            cmd.MangaId.Should().Be(42);
            cmd.Monitored.Should().BeTrue();
        }

        [Test]
        public void MangaRssSyncCommand_extends_command_with_signalr_updates()
        {
            var cmd = new MangaRssSyncCommand();

            cmd.Should().BeAssignableTo<Command>();
            cmd.SendUpdatesToClient.Should().BeTrue();
        }

        [Test]
        public void IMangaSearchForReleases_exposes_MangaSearch_and_ChapterSearch()
        {
            var iface = typeof(IMangaSearchForReleases);

            iface.GetMethod("MangaSearch").Should().NotBeNull("interface must expose MangaSearch(MangaSearchCriteria)");
            iface.GetMethod("ChapterSearch").Should().NotBeNull("interface must expose ChapterSearch(ChapterSearchCriteria)");
        }
    }
}
