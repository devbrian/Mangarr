using System.Data;
using System.Data.Common;
using System.Data.SQLite;
using System.Linq;
using System.Threading;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ThingiProvider
{
    /// <summary>
    /// Regression coverage for CR-01 review finding CR-02: <c>ProviderRepository&lt;T&gt;</c>
    /// overrides <see cref="BasicRepository{TModel}.Query(SqlBuilder)"/> with its own
    /// connection / reader loop, so without an explicit retry wrap the SQLITE_BUSY retry
    /// coverage that BasicRepository&lt;T&gt; gives the read path is bypassed for every
    /// ThingiProvider repository (Indexers, DownloadClients, Notifications, Metadata,
    /// ImportLists). This fixture pins the wrap so it cannot regress silently.
    ///
    /// Strategy mirrors <c>BasicRepositoryRetryFixture</c>: a mocked IMainDatabase
    /// throws SQLiteException(Busy) on the first OpenConnection() call and returns a
    /// real in-memory SQLite connection thereafter. The connection is wrapped in a
    /// non-disposing facade so the repository's <c>using</c> block does not close it
    /// between retry attempts.
    /// </summary>
    [TestFixture]
    public class ProviderRepositoryRetryFixture : CoreTest
    {
        private Mock<IMainDatabase> _databaseMock;
        private SQLiteConnection _realConnection;
        private int _openConnectionCallCount;

        [OneTimeSetUp]
        public void RegisterTableMappings()
        {
            // BasicRepository<TModel> looks up the table name via TableMapping.Mapper
            // in its constructor. Trigger the same static initialization that DbFactory
            // performs so IndexerDefinition resolves to "Indexers".
            // TableMapping.Map() is not idempotent (TableMapper.Entity throws on
            // duplicate keys), so only invoke it when the static Mapper has not yet
            // been built by an earlier DbFactory / DbTest fixture run.
            if (TableMapping.Mapper == null || TableMapping.Mapper.TableNameMapping(typeof(IndexerDefinition)) == null)
            {
                TableMapping.Map();
            }
        }

        [SetUp]
        public void Setup()
        {
            _openConnectionCallCount = 0;

            // In-memory SQLite database with the Indexers schema (verbatim from
            // 001_mangarr_baseline.cs lines 49-61, minus the IDENTITY column we add
            // explicitly). Pre-populate one row so the post-retry success path
            // returns a known result.
            _realConnection = new SQLiteConnection("Data Source=:memory:;Version=3;Pooling=False");
            _realConnection.Open();
            using (var cmd = _realConnection.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE ""Indexers"" (
                        ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                        ""Name"" TEXT,
                        ""Implementation"" TEXT,
                        ""Settings"" TEXT,
                        ""ConfigContract"" TEXT,
                        ""EnableRss"" INTEGER,
                        ""EnableSearch"" INTEGER,
                        ""EnableAutomaticSearch"" INTEGER,
                        ""EnableInteractiveSearch"" INTEGER,
                        ""Priority"" INTEGER NOT NULL DEFAULT 25,
                        ""DownloadClientId"" INTEGER NOT NULL DEFAULT 0,
                        ""Tags"" TEXT
                    );
                    INSERT INTO ""Indexers""
                        (""Name"", ""Implementation"", ""ConfigContract"", ""Settings"",
                         ""EnableRss"", ""EnableSearch"", ""EnableAutomaticSearch"", ""EnableInteractiveSearch"",
                         ""Priority"", ""DownloadClientId"", ""Tags"")
                    VALUES
                        ('test-indexer', 'TestImpl', NULL, NULL,
                         1, 1, 1, 1,
                         25, 0, '[]');";
                cmd.ExecuteNonQuery();
            }

            _databaseMock = new Mock<IMainDatabase>();
            _databaseMock.SetupGet(d => d.DatabaseType).Returns(DatabaseType.SQLite);
            _databaseMock.Setup(d => d.OpenConnection()).Returns(() =>
            {
                var current = Interlocked.Increment(ref _openConnectionCallCount);

                // First two attempts simulate SQLITE_BUSY; the third returns a real
                // connection. The retry math is initial + 2 retries = 3 calls in this
                // success-on-3rd-attempt scenario, which exercises the configured
                // exponential backoff path more meaningfully than a single-retry case.
                if (current <= 2)
                {
                    throw CreateBusyException();
                }

                return new NonDisposingDbConnection(_realConnection);
            });
        }

        [TearDown]
        public void TearDown()
        {
            _realConnection?.Dispose();
        }

        [Test]
        public void provider_repository_query_should_retry_on_sqlite_busy_then_succeed()
        {
            // IndexerRepository is the simplest ProviderRepository<T> subclass and
            // exercises the overridden Query path through All() -> base.Query(builder).
            var subject = new IndexerRepository(_databaseMock.Object, Mocker.Resolve<IEventAggregator>());

            var results = subject.All().ToList();

            // Initial + 2 retries = 3 OpenConnection invocations when the success
            // happens on the 3rd attempt. If ProviderRepository.Query were not
            // wrapped in RetryStrategy, the very first BUSY would surface to the
            // caller and this would be 1, not 3 — that is the regression this test
            // pins.
            _openConnectionCallCount.Should().Be(3,
                "first two calls throw SQLITE_BUSY; the inherited RetryStrategy must keep retrying so the override is not bypassed");
            results.Should().HaveCount(1, "the in-memory schema was seeded with exactly one indexer row");
            results[0].Name.Should().Be("test-indexer");

            // OnRetry callback emits Logger.Warn per retry; tolerated regardless of
            // whether BasicRepository's static logger captured it before LoggingTest
            // initialised.
            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public void provider_repository_query_should_not_retry_non_busy_sqlite_errors()
        {
            // Replace the OpenConnection setup so it throws a non-Busy error. The
            // retry predicate filters on Busy only; constraint errors must surface
            // immediately.
            _databaseMock.Reset();
            _databaseMock.SetupGet(d => d.DatabaseType).Returns(DatabaseType.SQLite);
            var nonBusyAttempts = 0;
            _databaseMock.Setup(d => d.OpenConnection()).Returns(() =>
            {
                Interlocked.Increment(ref nonBusyAttempts);
                throw new SQLiteException(SQLiteErrorCode.Constraint, "constraint violated");
            });

            var subject = new IndexerRepository(_databaseMock.Object, Mocker.Resolve<IEventAggregator>());

            Assert.Throws<SQLiteException>(() => subject.All().ToList());

            nonBusyAttempts.Should().Be(1, "non-Busy SQLiteException should not trigger Polly retry");
        }

        private static SQLiteException CreateBusyException()
        {
            return new SQLiteException(SQLiteErrorCode.Busy, "database is locked (simulated)");
        }

        /// <summary>
        /// Wraps a real DbConnection so that the repository's <c>using</c> blocks do
        /// not dispose/close it between retry attempts. Mirrors the helper in
        /// <c>BasicRepositoryRetryFixture</c>.
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
                // repository's `using` block between retry attempts.
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
