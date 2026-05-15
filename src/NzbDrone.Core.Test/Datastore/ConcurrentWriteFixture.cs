using System;
using System.Data.SQLite;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Datastore
{
    /// <summary>
    /// Plan 01-05 Task 3: integration smoke test that proves the BusyTimeout=5000
    /// (D-15 baseline floor) + Polly MaxRetryAttempts=3 combination prevents
    /// SQLITE_BUSY surfacing under realistic v1 parallel workloads. This is the
    /// observational evidence backing Phase 1 success criterion #4.
    ///
    /// Reuses the ScheduledTask model so the existing DbTest harness can wire the
    /// real SQLite database without bespoke TableMapping registration.
    ///
    /// WR-02 (#147): catch blocks are narrowed to <see cref="SQLiteException"/> so
    /// only the contention class the BusyTimeout + Polly stack is designed to
    /// swallow is counted. Any other exception type propagates and fails the test,
    /// preventing silent regressions (e.g. mapper / connection-string drift) from
    /// being mistaken for "no SQLITE_BUSY surfaced".
    /// </summary>
    [TestFixture]
    [Category("IntegrationTest")]
    public class ConcurrentWriteFixture : DbTest<BasicRepository<ScheduledTask>, ScheduledTask>
    {
        [Test]
        public void parallel_inserts_do_not_surface_sqlite_busy()
        {
            const int writers = 8;
            const int writesPerThread = 10;
            var failures = 0;

            // 8 threads x 10 inserts = 80 concurrent operations against a single
            // SQLite database. With BusyTimeout=5000ms + Polly MaxRetryAttempts=3,
            // transient SQLITE_BUSY events should never reach the caller under v1's
            // expected single-process workload.
            Parallel.For(0, writers, t =>
            {
                for (var i = 0; i < writesPerThread; i++)
                {
                    try
                    {
                        var task = Builder<ScheduledTask>
                            .CreateNew()
                            .With(x => x.Id = 0)
                            .With(x => x.TypeName = $"Concurrent.Writer{t}.Op{i}")
                            .With(x => x.Interval = (t * 100) + i)
                            .With(x => x.LastExecution = DateTime.UtcNow)
                            .With(x => x.LastStartTime = DateTime.UtcNow)
                            .Build();

                        Subject.Insert(task);
                    }
                    catch (SQLiteException)
                    {
                        // WR-02 (#147): only count SQLite contention surfacing; any
                        // other exception type propagates and fails the test.
                        Interlocked.Increment(ref failures);
                    }
                }
            });

            failures.Should().Be(0,
                "BusyTimeout=5000 + Polly MaxRetryAttempts=3 should swallow all transient SQLITE_BUSY events");
            Subject.All().Count().Should().Be(writers * writesPerThread);

            // Polly OnRetry callback may emit Logger.Warn under contention; tolerated.
            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public void parallel_reads_under_write_pressure_do_not_throw()
        {
            const int writers = 4;
            const int readers = 4;
            const int duration = 25;

            // Seed the table so reads have something to scan.
            for (var i = 0; i < 10; i++)
            {
                Subject.Insert(Builder<ScheduledTask>
                    .CreateNew()
                    .With(x => x.Id = 0)
                    .With(x => x.TypeName = $"Seed.Op{i}")
                    .With(x => x.LastExecution = DateTime.UtcNow)
                    .With(x => x.LastStartTime = DateTime.UtcNow)
                    .Build());
            }

            var writeFailures = 0;
            var readFailures = 0;

            Parallel.Invoke(
                () => Parallel.For(0, writers, t =>
                {
                    for (var i = 0; i < duration; i++)
                    {
                        try
                        {
                            Subject.Insert(Builder<ScheduledTask>
                                .CreateNew()
                                .With(x => x.Id = 0)
                                .With(x => x.TypeName = $"Writer{t}.Op{i}")
                                .With(x => x.LastExecution = DateTime.UtcNow)
                                .With(x => x.LastStartTime = DateTime.UtcNow)
                                .Build());
                        }
                        catch (SQLiteException)
                        {
                            // WR-02 (#147): only count SQLite contention surfacing.
                            Interlocked.Increment(ref writeFailures);
                        }
                    }
                }),
                () => Parallel.For(0, readers, t =>
                {
                    for (var i = 0; i < duration; i++)
                    {
                        try
                        {
                            // Exercise the new read-path retry wraps (Query funnel + Count).
                            var rows = Subject.All().ToList();
                            var rowCount = Subject.Count();
                            rows.Should().HaveCountGreaterOrEqualTo(0);
                            rowCount.Should().BeGreaterOrEqualTo(0);
                        }
                        catch (SQLiteException)
                        {
                            // WR-02 (#147): only count SQLite contention surfacing.
                            Interlocked.Increment(ref readFailures);
                        }
                    }
                }));

            writeFailures.Should().Be(0, "writers under read pressure should not surface SQLITE_BUSY");
            readFailures.Should().Be(0, "readers under write pressure should not surface SQLITE_BUSY");

            ExceptionVerification.IgnoreWarns();
        }
    }
}
