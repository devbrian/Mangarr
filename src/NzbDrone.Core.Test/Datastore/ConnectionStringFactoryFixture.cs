using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore
{
    [TestFixture]
    public class ConnectionStringFactoryFixture : CoreTest<ConnectionStringFactory>
    {
        [SetUp]
        public void Setup()
        {
            // Force Sqlite branch in ConnectionStringFactory by ensuring all postgres
            // configuration values are empty/whitespace. Default Moq returns null for
            // unmocked string properties; we set up explicit empties for clarity.
            Mocker.GetMock<IConfigFileProvider>()
                  .SetupGet(c => c.PostgresHost).Returns(string.Empty);
            Mocker.GetMock<IConfigFileProvider>()
                  .SetupGet(c => c.PostgresMainDbConnectionString).Returns(string.Empty);
            Mocker.GetMock<IConfigFileProvider>()
                  .SetupGet(c => c.PostgresLogDbConnectionString).Returns(string.Empty);
            Mocker.GetMock<IConfigFileProvider>()
                  .SetupGet(c => c.LogDbEnabled).Returns(false);

            // GetDatabase() / GetLogDatabase() are extension methods on IAppFolderInfo
            // that resolve to Path.Combine(AppDataFolder, "sonarr.db" | "logs.db").
            // Provide a deterministic AppDataFolder for the test.
            Mocker.GetMock<IAppFolderInfo>()
                  .SetupGet(a => a.AppDataFolder).Returns(@"C:\test\data");
        }

        [Test]
        public void busytimeout_5000_in_sqlite_connection_string()
        {
            // The MainDbConnection / LogDbConnection are eagerly populated in the
            // ConnectionStringFactory constructor (via Sqlite branch given the postgres
            // setup above). Reading either is sufficient to assert BusyTimeout=5000.
            // SQLiteConnectionStringBuilder encodes keys in lowercase, hence "busytimeout=5000".
            var info = Subject.MainDbConnection;

            info.ConnectionString.Should().Contain("busytimeout=5000");
            info.ConnectionString.Should().NotContain("busytimeout=1000");
        }

        [Test]
        public void busytimeout_5000_in_log_connection_string()
        {
            var info = Subject.LogDbConnection;

            info.ConnectionString.Should().Contain("busytimeout=5000");
            info.ConnectionString.Should().NotContain("busytimeout=1000");
        }

        [Test]
        public void preserves_other_sqlite_settings()
        {
            var info = Subject.MainDbConnection;

            // SQLiteConnectionStringBuilder encodes keys in lowercase but values keep their casing,
            // so we assert "pooling=True" / "version=3" verbatim.
            info.ConnectionString.Should().Contain("pooling=True");
            info.ConnectionString.Should().Contain("version=3");
        }

        [Test]
        public void main_connection_uses_sqlite_database_type()
        {
            Subject.MainDbConnection.DatabaseType.Should().Be(DatabaseType.SQLite);
        }
    }
}
