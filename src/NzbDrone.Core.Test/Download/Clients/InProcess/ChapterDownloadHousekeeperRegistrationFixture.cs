using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download.Clients.InProcess;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.Clients.InProcess
{
    /// <summary>
    /// Phase 4 plan 04-08 Task 1 — anti-pattern C / Phase 2 RefreshScheduledTaskFixture lesson:
    /// HousekeepInProcessDownloadsCommand MUST be registered via TaskManager.defaultTasks at
    /// runtime, NOT seeded via Migration 001. The migration-seed approach silently passes
    /// in-isolation tests but doesn't actually register at runtime (Phase 2 audit lesson).
    ///
    /// WARNING #10 fix: previous design used <c>File.ReadAllText</c> against the migration
    /// source — that's anti-pattern A (source-text grep) even when guarding against
    /// anti-pattern C. This fixture uses the DbTest harness (real migrated SQLite) and
    /// asserts via two raw SQL queries:
    ///   1. Post-migration / pre-Init: zero rows for the housekeeper TypeName.
    ///   2. After TaskManager.Handle(ApplicationStartedEvent): row appears.
    /// Together those two assertions prove the migration is NOT the registration source AND
    /// TaskManager.defaultTasks IS.
    /// </summary>
    [TestFixture]
    public class ChapterDownloadHousekeeperRegistrationFixture : DbTest
    {
        private static readonly string HousekeeperTypeName =
            typeof(HousekeepInProcessDownloadsCommand).FullName;

        private int CountScheduledTasksFor(string typeName)
        {
            using (var conn = Db.OpenConnection())
            {
                // CI-infra F8 fix (ci-test-jobs-latent-faults): quote the table + column
                // identifiers. Unquoted, Postgres folds them to lowercase (`scheduledtasks`
                // / `typename`) and the migrated schema's mixed-case `"ScheduledTasks"` /
                // `"TypeName"` are not found — `42P01: relation "scheduledtasks" does not
                // exist` in the unit_test_postgres job. Double-quoted identifiers are valid
                // on SQLite too, so this works on both backends.
                return conn.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM \"ScheduledTasks\" WHERE \"TypeName\" = @t",
                    new { t = typeName });
            }
        }

        [Test]
        public void HousekeepInProcessDownloadsCommand_is_NOT_seeded_via_Migration_001()
        {
            // Real SQLite, all migrations applied via DbTest [SetUp] → SetupDb. We have NOT
            // called TaskManager.Handle yet, so the only way a row exists is if the migration
            // seeded it (anti-pattern C).
            CountScheduledTasksFor(HousekeeperTypeName).Should().Be(0,
                "anti-pattern C: 001_mangarr_baseline.cs MUST NOT seed ScheduledTasks for the Phase 4 housekeeper");
        }

        [Test]
        public void HousekeepInProcessDownloadsCommand_is_in_TaskManager_defaultTasks()
        {
            // Sanity: pre-Init no row.
            CountScheduledTasksFor(HousekeeperTypeName).Should().Be(0);

            // Construct TaskManager with the real migrated DB-backed ScheduledTaskRepository
            // and dispatch the ApplicationStartedEvent — same path the host uses at startup.
            var scheduledTaskRepository = Mocker.Resolve<ScheduledTaskRepository>();
            var taskManager = new TaskManager(
                scheduledTaskRepository,
                Mocker.GetMock<IConfigService>().Object,
                Mocker.Resolve<CacheManager>(),
                TestLogger);

            taskManager.Handle(new ApplicationStartedEvent());

            // After Init the row must exist — proving TaskManager.defaultTasks is the
            // registration source (NOT the migration).
            CountScheduledTasksFor(HousekeeperTypeName).Should().BeGreaterThan(0,
                "TaskManager.defaultTasks MUST be the registration source for HousekeepInProcessDownloadsCommand");

            // And the cached instance reflects the same row.
            taskManager.GetAll().Should().Contain(t => t.TypeName == HousekeeperTypeName,
                "the in-memory TaskManager cache must surface the housekeeper alongside DB persistence");
        }
    }
}
