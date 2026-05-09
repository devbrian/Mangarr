using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FluentValidation.Results;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients.InProcess;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Test.Download.DownloadClientFactoryTests
{
    // Sonarr divergence: zero-config first-run UX (PROJECT.md v1 lock). Mangarr seeds the
    // bundled InProcessImageDownloadClient on a fresh DB. Mirrors MetadataSourceFactoryFixture's
    // seed + idempotency style.
    [TestFixture]
    public class InitializeProvidersFixture : CoreTest<DownloadClientFactory>
    {
        private List<DownloadClientDefinition> _stored;
        private List<IDownloadClient> _providers;

        [SetUp]
        public void Setup()
        {
            _stored = new List<DownloadClientDefinition>();

            // Stub provider whose GetType().Name == "InProcessImageDownloadClient". The seed
            // code only consults GetType().Name, .Name, and .ConfigContract on the provider,
            // so a stub suffices (the real client requires a heavy DI graph). Settings are
            // a real InProcessImageDownloadClientSettings instance — its compile-time
            // defaults (DownloadsPerSource=2, PagesPerChapter=4, RetentionDays=7) are part
            // of the seed contract.
            _providers = new List<IDownloadClient>
            {
                new InProcessImageDownloadClient(),
            };

            Mocker.GetMock<IDownloadClientRepository>()
                  .Setup(r => r.All())
                  .Returns(() => _stored.ToList());

            Mocker.GetMock<IDownloadClientRepository>()
                  .Setup(r => r.Insert(It.IsAny<DownloadClientDefinition>()))
                  .Callback<DownloadClientDefinition>(d =>
                  {
                      d.Id = _stored.Count + 1;
                      _stored.Add(d);
                  })
                  .Returns<DownloadClientDefinition>(d => d);

            Mocker.SetConstant<IEnumerable<IDownloadClient>>(_providers);
        }

        [Test]
        public void Handle_ApplicationStarted_seeds_in_process_client_on_empty_db()
        {
            Subject.Handle(new ApplicationStartedEvent());

            _stored.Should().HaveCount(1);
            var seeded = _stored[0];
            seeded.Implementation.Should().Be(nameof(InProcessImageDownloadClient));
            seeded.Name.Should().Be("Mangarr In-Process Downloader");
            seeded.ConfigContract.Should().Be(nameof(InProcessImageDownloadClientSettings));
            seeded.Enable.Should().BeTrue();
            seeded.Priority.Should().Be(1);
            seeded.RemoveCompletedDownloads.Should().BeTrue();
            seeded.RemoveFailedDownloads.Should().BeTrue();

            // Settings POCO compile-time defaults are part of the seed contract.
            var settings = seeded.Settings.Should().BeOfType<InProcessImageDownloadClientSettings>().Subject;
            settings.DownloadsPerSource.Should().Be(2);
            settings.PagesPerChapter.Should().Be(4);
            settings.RetentionDays.Should().Be(7);
        }

        [Test]
        public void Handle_ApplicationStarted_is_idempotent_when_rows_already_exist()
        {
            // User-edited row — must NOT be touched on next start.
            _stored.Add(new DownloadClientDefinition
            {
                Id = 1,
                Name = "User Custom Downloader",
                Implementation = nameof(InProcessImageDownloadClient),
                ConfigContract = nameof(InProcessImageDownloadClientSettings),
                Enable = false,
                Priority = 9,
            });

            Subject.Handle(new ApplicationStartedEvent());

            _stored.Should().HaveCount(1);
            _stored[0].Name.Should().Be("User Custom Downloader");
            _stored[0].Enable.Should().BeFalse();
            _stored[0].Priority.Should().Be(9);
        }

        // Minimal IDownloadClient stub: only the IProvider members consulted by
        // InitializeProviders.
        private sealed class InProcessImageDownloadClient : IDownloadClient
        {
            public string Name => "Mangarr In-Process Downloader";
            public Type ConfigContract => typeof(InProcessImageDownloadClientSettings);
            public ProviderMessage Message => null;
            public IEnumerable<ProviderDefinition> DefaultDefinitions => new List<ProviderDefinition>();
            public ProviderDefinition Definition { get; set; }

            public DownloadProtocol Protocol => DownloadProtocol.Http;

            public System.Threading.Tasks.Task<string> Download(RemoteChapter remoteChapter, IIndexer indexer)
                => System.Threading.Tasks.Task.FromResult<string>(null);

            public IEnumerable<DownloadClientItem> GetItems() => Array.Empty<DownloadClientItem>();
            public DownloadClientItem GetImportItem(DownloadClientItem item, DownloadClientItem prev) => item;
            public void RemoveItem(DownloadClientItem item, bool deleteData)
            {
            }

            public DownloadClientInfo GetStatus() => new() { IsLocalhost = true, OutputRootFolders = new List<NzbDrone.Common.Disk.OsPath>() };

            public void MarkItemAsImported(DownloadClientItem downloadClientItem)
            {
            }

            public ValidationResult Test() => new();
            public object RequestAction(string s, IDictionary<string, string> q) => null;
        }
    }
}
