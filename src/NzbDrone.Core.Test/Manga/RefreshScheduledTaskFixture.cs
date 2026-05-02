using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MangaTests
{
    // Wave 0 scaffold — verifies the ScheduledTasks row inserted by Migration 002 has
    // Interval = 720 (12h, per D-18) and TypeName = 'NzbDrone.Core.Manga.Commands.RefreshMangaCommand'.
    // RED until the migration body actually inserts the row AND Plan 02-09 lands the
    // RefreshMangaCommand class.
    [TestFixture]
    public class RefreshScheduledTaskFixture : CoreTest
    {
        // The ScheduledTasks row inserted by Migration 002 must have:
        //   Interval = 720
        //   TypeName = "NzbDrone.Core.Manga.Commands.RefreshMangaCommand"
        [Test]
        [Ignore("RED — Migration 002 ScheduledTasks insert + Plan 02-09 RefreshMangaCommand class.")]
        public void Migration_002_inserts_RefreshMangaCommand_row_with_Interval_720()
            => Assert.Inconclusive("Plan 02-09 + Migration 002 body");
    }
}
