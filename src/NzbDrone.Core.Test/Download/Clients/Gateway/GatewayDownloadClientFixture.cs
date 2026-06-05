using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients.Gateway;
using NzbDrone.Core.Download.Clients.Gateway.Responses;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.Clients.Gateway
{
    /// <summary>
    /// Wave 0 fixture for <see cref="GatewayDownloadClient"/> (GWDL-01/02/03) — mirrors the
    /// <c>InProcessImageDownloadClientFixture</c> shape (<see cref="CoreTest{T}"/>, mock
    /// <see cref="IGatewayDownloadProxy"/>, set <c>Subject.Definition</c>). References the
    /// NOT-YET-BUILT production type and compiles GREEN only after Task 3 (contract-first ordering).
    ///
    /// Coverage:
    /// - GWDL-02: Download returns the gateway jobId; a null jobId throws
    ///   <see cref="DownloadClientRejectedReleaseException"/>.
    /// - GWDL-03: MapStatus table (all 8 wire values + unknown); GetStatus remaps every
    ///   OutputRootFolder through <see cref="IRemotePathMappingService"/>.
    /// - GWDL-01: Test() HARD-fails when an output folder is unreachable AND isLocalhost is false.
    /// </summary>
    [TestFixture]
    public class GatewayDownloadClientFixture : CoreTest<GatewayDownloadClient>
    {
        private Mock<IGatewayDownloadProxy> _proxy;
        private GatewayDownloadClientSettings _settings;

        [SetUp]
        public void Setup()
        {
            _proxy = Mocker.GetMock<IGatewayDownloadProxy>();

            _settings = new GatewayDownloadClientSettings
            {
                Host = "gateway-host",
                Port = 9191,
                ApiKey = "test-api-key"
            };

            Subject.Definition = new DownloadClientDefinition
            {
                Id = 1,
                Name = "Test Gateway",
                Settings = _settings,
                ConfigContract = nameof(GatewayDownloadClientSettings)
            };

            // Identity remap by default — overridden per-test where remap behavior is asserted.
            Mocker.GetMock<IRemotePathMappingService>()
                  .Setup(s => s.RemapRemoteToLocal(It.IsAny<string>(), It.IsAny<OsPath>()))
                  .Returns<string, OsPath>((_, p) => p);
        }

        private RemoteChapter BuildRemoteChapter()
        {
            return new RemoteChapter
            {
                Release = new ReleaseInfo
                {
                    // The gateway mints guids as "{sourceKey}:{mangaId}:ch-{n}:{lang}:{chapterId}";
                    // DownloadUrl carries the opaque R6 handle (GatewayParser.cs:80). Indexer is the
                    // Mangarr display name — deliberately NOT the gateway sourceKey (GH #310).
                    Guid = "mangadex:m-1:ch-2:en:c-2",
                    DownloadUrl = "opaque-handle",
                    Indexer = "Mangarr Gateway"
                }
            };
        }

        [Test]
        public void Protocol_is_Http()
        {
            Subject.Protocol.Should().Be(DownloadProtocol.Http);
        }

        [Test]
        public void Name_is_canonical()
        {
            Subject.Name.Should().Be("Mangarr Gateway");
        }

        [Test]
        public async Task Download_returns_jobId_from_submit()
        {
            _proxy.Setup(p => p.Submit(It.IsAny<GatewaySubmitRequest>(), _settings))
                  .Returns(new GatewaySubmitResponse { JobId = "abc" });

            var downloadId = await Subject.Download(BuildRemoteChapter(), Mocker.GetMock<IIndexer>().Object);

            downloadId.Should().Be("abc");
        }

        [Test]
        public void Download_submits_opaque_handle_and_cbz_format()
        {
            GatewaySubmitRequest captured = null;
            _proxy.Setup(p => p.Submit(It.IsAny<GatewaySubmitRequest>(), _settings))
                  .Callback<GatewaySubmitRequest, GatewayDownloadClientSettings>((req, _) => captured = req)
                  .Returns(new GatewaySubmitResponse { JobId = "abc" });

            Subject.Download(BuildRemoteChapter(), Mocker.GetMock<IIndexer>().Object).GetAwaiter().GetResult();

            captured.Should().NotBeNull();

            // GH #310: ReleaseHandle is the opaque R6 handle (Release.DownloadUrl), NOT Release.Guid;
            // SourceKey is the originating gateway source (guid prefix), NOT the Mangarr indexer name.
            captured.ReleaseHandle.Should().Be("opaque-handle");
            captured.SourceKey.Should().Be("mangadex");
            captured.OutputFormat.Should().Be("cbz");
        }

        [Test]
        public async Task Download_null_jobId_throws_rejected_release()
        {
            _proxy.Setup(p => p.Submit(It.IsAny<GatewaySubmitRequest>(), _settings))
                  .Returns(new GatewaySubmitResponse { JobId = null, Message = "expired handle" });

            var act = () => Subject.Download(BuildRemoteChapter(), Mocker.GetMock<IIndexer>().Object);

            await act.Should().ThrowAsync<DownloadClientRejectedReleaseException>();
        }

        [Test]
        [TestCase("queued", DownloadItemStatus.Queued)]
        [TestCase("resolving", DownloadItemStatus.Queued)]
        [TestCase("downloading", DownloadItemStatus.Downloading)]
        [TestCase("archiving", DownloadItemStatus.Downloading)]
        [TestCase("completed", DownloadItemStatus.Completed)]
        [TestCase("failed", DownloadItemStatus.Failed)]
        [TestCase("warning", DownloadItemStatus.Warning)]
        [TestCase("paused", DownloadItemStatus.Paused)]
        [TestCase("anything-unknown", DownloadItemStatus.Warning)]
        public void MapStatus_maps_each_wire_status(string wire, DownloadItemStatus expected)
        {
            _proxy.Setup(p => p.GetJobs(_settings))
                  .Returns(new GatewayJobList { Jobs = new List<GatewayJob> { new GatewayJob { JobId = "j", Title = "t", Status = wire } } });

            var item = Subject.GetItems().Single();

            item.Status.Should().Be(expected);
        }

        [Test]
        public void GetItems_remaps_completed_output_path()
        {
            _proxy.Setup(p => p.GetJobs(_settings))
                  .Returns(new GatewayJobList
                  {
                      Jobs = new List<GatewayJob>
                      {
                          new GatewayJob { JobId = "j1", Title = "Ch 1", Status = "completed", OutputPath = "/remote/ch1.cbz" }
                      }
                  });

            Mocker.GetMock<IRemotePathMappingService>()
                  .Setup(s => s.RemapRemoteToLocal("gateway-host", It.Is<OsPath>(p => p.FullPath == "/remote/ch1.cbz")))
                  .Returns(new OsPath("/local/ch1.cbz"));

            var item = Subject.GetItems().Single();

            item.OutputPath.FullPath.Should().Be("/local/ch1.cbz");
        }

        [Test]
        public void GetItems_maps_byte_counters_onto_size_fields()
        {
            // GH #307: the gateway job's totalBytes/remainingBytes MUST flow onto the
            // DownloadClientItem so the queue projection (Size=TotalSize / SizeLeft=RemainingSize)
            // and the Activity Queue progress bar render. Pre-fix GetItems left both 0 → no bar.
            _proxy.Setup(p => p.GetJobs(_settings))
                  .Returns(new GatewayJobList
                  {
                      Jobs = new List<GatewayJob>
                      {
                          new GatewayJob
                          {
                              JobId = "j1",
                              Title = "Ch 1",
                              Status = "completed",
                              TotalBytes = 27439825,
                              RemainingBytes = 0
                          }
                      }
                  });

            var item = Subject.GetItems().Single();

            item.TotalSize.Should().Be(27439825);
            item.RemainingSize.Should().Be(0);
        }

        [Test]
        public void GetStatus_remaps_every_output_root_folder()
        {
            _proxy.Setup(p => p.GetStatus(_settings))
                  .Returns(new GatewayStatusResponse
                  {
                      IsLocalhost = false,
                      RemovesCompletedDownloads = true,
                      OutputRootFolders = new List<string> { "/remote/a", "/remote/b" }
                  });

            var status = Subject.GetStatus();

            status.IsLocalhost.Should().BeFalse();
            status.RemovesCompletedDownloads.Should().BeTrue();
            Mocker.GetMock<IRemotePathMappingService>()
                  .Verify(s => s.RemapRemoteToLocal("gateway-host", It.IsAny<OsPath>()), Times.Exactly(2));
            status.OutputRootFolders.Should().HaveCount(2);
        }

        [Test]
        public void RemoveItem_delegates_to_proxy()
        {
            var item = new DownloadClientItem { DownloadId = "job-7" };

            Subject.RemoveItem(item, deleteData: true);

            _proxy.Verify(p => p.RemoveJob("job-7", true, _settings), Times.Once);
        }

        [Test]
        public void Test_hard_fails_when_output_folder_unreachable_and_not_localhost()
        {
            _proxy.Setup(p => p.GetVersion(_settings)).Returns("1.0.0");
            _proxy.Setup(p => p.GetStatus(_settings))
                  .Returns(new GatewayStatusResponse
                  {
                      IsLocalhost = false,
                      OutputRootFolders = new List<string> { "/remote/unreachable" }
                  });

            // Folder does not exist → TestFolder returns a failure.
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.FolderExists(It.IsAny<string>()))
                  .Returns(false);

            var result = Subject.Test();

            result.IsValid.Should().BeFalse();
        }

        [Test]
        public void Test_hard_fails_gracefully_when_gateway_reports_host_invalid_path()
        {
            // Regression (live-gateway human-verify, Phase 38): a Dockerized gateway reports a
            // container-internal path (e.g. "/data/manga") that is NOT a valid path on the Mangarr
            // host OS. On Windows the path layer throws ArgumentException ("not a valid Windows
            // path") from inside TestFolder — which previously escaped the foreach (it sat outside
            // the version/status try/catch) and surfaced as an opaque "Test was aborted due to an
            // error". The output-folder check must convert that throw into the SAME actionable
            // "Configure a Remote Path Mapping" hard-fail, never let it abort the whole Test.
            _proxy.Setup(p => p.GetVersion(_settings)).Returns("1.0.0");
            _proxy.Setup(p => p.GetStatus(_settings))
                  .Returns(new GatewayStatusResponse
                  {
                      IsLocalhost = false,
                      OutputRootFolders = new List<string> { "/data/manga" }
                  });

            // The host OS path layer rejects the container path — TestFolder throws rather than
            // returning a failure.
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.FolderExists(It.IsAny<string>()))
                  .Throws(new ArgumentException("value [/data/manga] is not a valid Windows path. paths must be a full path eg. C:\\Windows"));

            // Must not throw — the Test wrapper returns a graceful invalid result.
            var result = Subject.Test();

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Remote Path Mapping"));
        }

        [Test]
        public void Test_passes_for_localhost_even_if_folder_unreachable()
        {
            _proxy.Setup(p => p.GetVersion(_settings)).Returns("1.0.0");
            _proxy.Setup(p => p.GetStatus(_settings))
                  .Returns(new GatewayStatusResponse
                  {
                      IsLocalhost = true,
                      OutputRootFolders = new List<string> { "/remote/unreachable" }
                  });

            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.FolderExists(It.IsAny<string>()))
                  .Returns(false);

            var result = Subject.Test();

            result.IsValid.Should().BeTrue();
        }

        [Test]
        public void Auto_discovered_via_provider_factory()
        {
            typeof(GatewayDownloadClient).Should().BeAssignableTo<IDownloadClient>();
            typeof(GatewayDownloadClient).IsPublic.Should().BeTrue();
        }
    }
}
