using System;
using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    [TestFixture]
    public class RemotePathMappingCheckFixture : CoreTest<RemotePathMappingCheck>
    {
        private string _downloadRootPath = @"c:\Test".AsOsAgnostic();
        private string _downloadItemPath = @"c:\Test\item".AsOsAgnostic();

        private DownloadClientInfo _clientStatus;
        private DownloadClientItem _downloadItem;
        private Mock<IDownloadClient> _downloadClient;

        private static Exception[] DownloadClientExceptions =
        {
            new DownloadClientUnavailableException("error"),
            new DownloadClientAuthenticationException("error"),
            new DownloadClientException("error")
        };

        [SetUp]
        public void Setup()
        {
            _downloadItem = new DownloadClientItem
            {
                DownloadClientInfo = new DownloadClientItemClientInfo { Name = "Test" },
                DownloadId = "TestId",
                OutputPath = new OsPath(_downloadItemPath)
            };

            _clientStatus = new DownloadClientInfo
            {
                IsLocalhost = true,
                OutputRootFolders = new List<OsPath> { new OsPath(_downloadRootPath) }
            };

            _downloadClient = Mocker.GetMock<IDownloadClient>();
            _downloadClient.Setup(s => s.Definition)
                .Returns(new DownloadClientDefinition { Name = "Test" });

            _downloadClient.Setup(s => s.GetItems())
                .Returns(new List<DownloadClientItem> { _downloadItem });

            _downloadClient.Setup(s => s.GetStatus())
                .Returns(_clientStatus);

            Mocker.GetMock<IProvideDownloadClient>()
                  .Setup(s => s.GetDownloadClients(It.IsAny<bool>()))
                  .Returns(new IDownloadClient[] { _downloadClient.Object });

            Mocker.GetMock<IConfigService>()
                  .Setup(s => s.EnableCompletedDownloadHandling)
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.FolderExists(It.IsAny<string>()))
                .Returns((string path) =>
                {
                    Ensure.That(path, () => path).IsValidPath(PathValidationType.CurrentOs);
                    return false;
                });

            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.FileExists(It.IsAny<string>()))
                .Returns((string path) =>
                {
                    Ensure.That(path, () => path).IsValidPath(PathValidationType.CurrentOs);
                    return false;
                });

            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(It.IsAny<string>()))
                  .Returns("Some Warning Message");
        }

        private void GivenFolderExists(string folder)
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.FolderExists(folder))
                .Returns(true);
        }

        private void GivenFileExists(string file)
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.FileExists(file))
                .Returns(true);
        }

        private void GivenDocker()
        {
            Mocker.GetMock<IOsInfo>()
                .Setup(x => x.IsDocker)
                .Returns(true);
        }

        [Test]
        public void should_return_ok_if_setup_correctly()
        {
            GivenFolderExists(_downloadRootPath);

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_return_permissions_error_if_local_client_download_root_missing()
        {
            Subject.Check().ShouldBeError(wikiFragment: "permissions-error");
        }

        [Test]
        public void should_return_mapping_error_if_remote_client_root_path_invalid()
        {
            _clientStatus.IsLocalhost = false;
            _clientStatus.OutputRootFolders = new List<OsPath> { new OsPath("An invalid path") };

            Subject.Check().ShouldBeError(wikiFragment: "bad-remote-path-mapping");
        }

        [Test]
        public void should_return_download_client_error_if_local_client_root_path_invalid()
        {
            _clientStatus.IsLocalhost = true;
            _clientStatus.OutputRootFolders = new List<OsPath> { new OsPath("An invalid path") };

            Subject.Check().ShouldBeError(wikiFragment: "bad-download-client-settings");
        }

        [Test]
        public void should_return_path_mapping_error_if_remote_client_download_root_missing()
        {
            _clientStatus.IsLocalhost = false;

            Subject.Check().ShouldBeError(wikiFragment: "bad-remote-path-mapping");
        }

        [Test]
        [TestCaseSource("DownloadClientExceptions")]
        public void should_return_ok_if_client_throws_downloadclientexception(Exception ex)
        {
            _downloadClient.Setup(s => s.GetStatus())
                .Throws(ex);

            Subject.Check().ShouldBeOk();

            ExceptionVerification.ExpectedErrors(0);
        }

        [Test]
        public void should_return_docker_path_mapping_error_if_on_docker_and_root_missing()
        {
            GivenDocker();

            Subject.Check().ShouldBeError(wikiFragment: "docker-bad-remote-path-mapping");
        }
    }
}
