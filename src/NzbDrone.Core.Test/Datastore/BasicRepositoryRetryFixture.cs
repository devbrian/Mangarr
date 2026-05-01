using System.Data;
using System.Data.Common;
using System.Data.SQLite;
using System.Threading;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Datastore
{
    /// <summary>
    /// Verifies that the Polly RetryStrategy in BasicRepository{T} actually fires
    /// on SQLITE_BUSY for the read/delete/upsert paths added in Plan 01-05 Task 2.
    /// Uses a mocked IDatabase that throws SQLiteException(Busy) the first time
    /// OpenConnection() is invoked, then delegates to a real in-memory SQLite
    /// connection on subsequent calls so the underlying Dapper plumbing still
    /// has a valid connection to work with.
    /// </summary>
    [TestFixture]
    public class BasicRepositoryRetryFixture : CoreTest
    {
        private Mock<IDatabase> _databaseMock;
        private SQLiteConnection _realConnection;
        private int _openConnectionCallCount;

        [OneTimeSetUp]
        public void RegisterTableMappings()
        {
            // BasicRepository<TModel> looks up the table name via TableMapping.Mapper
            // in its constructor. Trigger the same static initialization that DbFactory
            // performs so ScheduledTask resolves to "ScheduledTasks".
            // TableMapping.Map() is not idempotent (TableMapper.Entity throws on duplicate
            // keys), so we only invoke it when the static Mapper has not yet been built
            // by an earlier DbFactory / DbTest fixture run.
            if (TableMapping.Mapper == null || TableMapping.Mapper.TableNameMapping(typeof(ScheduledTask)) == null)
            {
                TableMapping.Map();
            }
        }

        [SetUp]
        public void Setup()
        {
            _openConnectionCallCount = 0;

            // In-memory SQLite database with a stub table matching ScheduledTask
            // so that conn.Execute(...) calls against the table do not throw schema
            // errors when the retry succeeds on the second attempt.
            _realConnection = new SQLiteConnection("Data Source=:memory:;Version=3;Pooling=False");
            _realConnection.Open();
            using (var cmd = _realConnection.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE ""ScheduledTasks"" (
                        ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                        ""TypeName"" TEXT,
                        ""Interval"" INTEGER,
                        ""LastExecution"" TEXT,
                        ""Priority"" INTEGER,
                        ""LastStartTime"" TEXT
                    );";
                cmd.ExecuteNonQuery();
            }

            _databaseMock = new Mock<IDatabase>();
            _databaseMock.SetupGet(d => d.DatabaseType).Returns(DatabaseType.SQLite);
            _databaseMock.Setup(d => d.OpenConnection()).Returns(() =>
            {
                var current = Interlocked.Increment(ref _openConnectionCallCount);
                if (current == 1)
                {
                    throw CreateBusyException();
                }

                // Return a non-disposing wrapper around the real connection so the
                // BasicRepository's `using (var conn = ...)` block doesn't close it
                // between retries. The wrapper swallows Dispose/Close and forwards
                // the rest to the real connection.
                return new NonDisposingDbConnection(_realConnection);
            });
        }

        [TearDown]
        public void TearDown()
        {
            _realConnection?.Dispose();
        }

        [Test]
        public void count_should_retry_on_sqlite_busy_then_succeed()
        {
            var subject = new BasicRepository<ScheduledTask>(_databaseMock.Object, Mocker.Resolve<IEventAggregator>());

            var result = subject.Count();

            _openConnectionCallCount.Should().Be(2, "first call throws SQLITE_BUSY; retry must invoke OpenConnection again");
            result.Should().Be(0);

            // OnRetry callback emits Logger.Warn per retry; tolerated regardless of whether
            // BasicRepository's static logger captured it before LoggingTest initialised.
            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public void purge_should_retry_on_sqlite_busy_then_succeed()
        {
            var subject = new BasicRepository<ScheduledTask>(_databaseMock.Object, Mocker.Resolve<IEventAggregator>());

            subject.Purge();

            _openConnectionCallCount.Should().Be(2, "first call throws SQLITE_BUSY; retry must invoke OpenConnection again");
            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public void retry_does_not_swallow_non_busy_sqlite_errors()
        {
            // Replace the OpenConnection setup so it throws a non-Busy error.
            _databaseMock.Reset();
            _databaseMock.SetupGet(d => d.DatabaseType).Returns(DatabaseType.SQLite);
            var nonBusyAttempts = 0;
            _databaseMock.Setup(d => d.OpenConnection()).Returns(() =>
            {
                Interlocked.Increment(ref nonBusyAttempts);
                throw new SQLiteException(SQLiteErrorCode.Constraint, "constraint violated");
            });

            var subject = new BasicRepository<ScheduledTask>(_databaseMock.Object, Mocker.Resolve<IEventAggregator>());

            Assert.Throws<SQLiteException>(() => subject.Count());

            // Predicate filters on Busy only; constraint errors must surface immediately
            // without exhausting retry budget.
            nonBusyAttempts.Should().Be(1, "non-Busy SQLiteException should not trigger Polly retry");
        }

        [Test]
        public void retry_gives_up_after_max_attempts_when_busy_persists()
        {
            // Always throw Busy. RetryStrategy is configured with MaxRetryAttempts=3,
            // which means the operation is invoked 1 + 3 = 4 times in total before the
            // exception bubbles out.
            _databaseMock.Reset();
            _databaseMock.SetupGet(d => d.DatabaseType).Returns(DatabaseType.SQLite);
            var attempts = 0;
            _databaseMock.Setup(d => d.OpenConnection()).Returns(() =>
            {
                Interlocked.Increment(ref attempts);
                throw CreateBusyException();
            });

            var subject = new BasicRepository<ScheduledTask>(_databaseMock.Object, Mocker.Resolve<IEventAggregator>());

            Assert.Throws<SQLiteException>(() => subject.Count());

            attempts.Should().Be(4, "MaxRetryAttempts=3 yields 1 initial + 3 retries = 4 total invocations");

            // 3 retries -> Logger.Warn invocations from OnRetry callback (count tolerated).
            ExceptionVerification.IgnoreWarns();
        }

        private static SQLiteException CreateBusyException()
        {
            // Public ctor for SQLiteException accepts (SQLiteErrorCode, string).
            return new SQLiteException(SQLiteErrorCode.Busy, "database is locked (simulated)");
        }

        /// <summary>
        /// Wraps a real DbConnection so that BasicRepository's `using` blocks do not
        /// dispose/close it between retry attempts. Dapper only calls a small surface
        /// of DbConnection (CreateCommand, State, Open) — we delegate those.
        /// </summary>
        private sealed class NonDisposingDbConnection : DbConnection
        {
            private readonly DbConnection _inner;

            public NonDisposingDbConnection(DbConnection inner)
            {
                _inner = inner;
            }

            public override string ConnectionString
            {
                get => _inner.ConnectionString;
                set => _inner.ConnectionString = value;
            }

            public override string Database => _inner.Database;

            public override string DataSource => _inner.DataSource;

            public override string ServerVersion => _inner.ServerVersion;

            public override ConnectionState State => _inner.State;

            public override void ChangeDatabase(string databaseName) => _inner.ChangeDatabase(databaseName);

            public override void Close()
            {
                // Intentionally a no-op so the inner connection survives the
                // BasicRepository's `using` block between retry attempts.
            }

            public override void Open()
            {
                if (_inner.State != ConnectionState.Open)
                {
                    _inner.Open();
                }
            }

            protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
                => _inner.BeginTransaction(isolationLevel);

            protected override DbCommand CreateDbCommand() => _inner.CreateCommand();

            protected override void Dispose(bool disposing)
            {
                // Intentionally do not dispose the inner connection here; the
                // outer fixture owns its lifecycle.
            }
        }
    }
}
