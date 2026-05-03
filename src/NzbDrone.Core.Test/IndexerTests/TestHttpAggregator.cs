using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Http;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Test.IndexerTests
{
    /// <summary>
    /// Test stub deriving from <see cref="HttpAggregatorBase{TSettings}"/>. Exposes the protected
    /// <c>FetchIndexerResponse</c> hook via <see cref="InvokeFetchIndexerResponse"/> so unit tests
    /// can assert on the HttpRequest emitted to <see cref="IHttpClient"/>.
    /// </summary>
    public class TestHttpAggregator : HttpAggregatorBase<TestHttpAggregatorSettings>
    {
        public override string Name => "Test Aggregator";

        public override string DefaultSourceKey => "test-source";

        // Plan 04 adds DownloadProtocol.Http; this test stub uses it.
        public override DownloadProtocol Protocol => DownloadProtocol.Http;

        public TestHttpAggregator(
            IHttpClient httpClient,
            IIndexerStatusService indexerStatusService,
            IIndexerSourceStatusService sourceStatusService,
            IConfigService configService,
            IParsingService parsingService,
            Logger logger,
            ILocalizationService localizationService)
            : base(httpClient, indexerStatusService, sourceStatusService, configService, parsingService, logger, localizationService)
        {
        }

        public IIndexerRequestGenerator _requestGenerator;

        public override IIndexerRequestGenerator GetRequestGenerator() => _requestGenerator;

        public IParseIndexerResponse _parser;

        public override IParseIndexerResponse GetParser() => _parser;

        // Phase 3 F-01 fix — expose the protected RecordX hooks for unit-test verification.
        public void InvokeRecordSuccess() => RecordSuccess();

        public void InvokeRecordFailure() => RecordFailure();

        public void InvokeRecordFailure(System.TimeSpan retryAfter) => RecordFailure(retryAfter);

        public void InvokeRecordConnectionFailure() => RecordConnectionFailure();

        // Phase 3 D-02 — concrete no-op stubs for the manga abstract surface.
        // Plan 03-02 fan-out missed this test helper; subclasses of HttpAggregatorBase
        // must implement Fetch(MangaSearchCriteria) and Fetch(ChapterSearchCriteria).
        // This test aggregator does not exercise manga search paths.
        public override Task<IList<ReleaseInfo>> Fetch(MangaSearchCriteria searchCriteria)
            => Task.FromResult<IList<ReleaseInfo>>(new List<ReleaseInfo>());

        public override Task<IList<ReleaseInfo>> Fetch(ChapterSearchCriteria searchCriteria)
            => Task.FromResult<IList<ReleaseInfo>>(new List<ReleaseInfo>());

        // Phase 4 D-01 — concrete no-op stub for the Phase 4 abstract surface. Plan 03-02
        // missed this test helper for the manga-Fetch fan-out; Phase 4 Plan 04-02's contract
        // test catches that miss again here. Tests that need to exercise the manifest path
        // assign GetChapterPagesImpl; default returns an empty manifest.
        public Func<ReleaseInfo, Task<ChapterManifest>> GetChapterPagesImpl { get; set; }

        public override Task<ChapterManifest> GetChapterPages(ReleaseInfo release)
            => GetChapterPagesImpl?.Invoke(release)
               ?? Task.FromResult(new ChapterManifest { Pages = Array.Empty<ChapterPage>(), TotalCount = 0 });

        /// <summary>
        /// Public passthrough to the protected <see cref="HttpAggregatorBase{TSettings}.FetchIndexerResponse"/>
        /// hook for unit-test assertions.
        /// </summary>
        public Task<IndexerResponse> InvokeFetchIndexerResponse(IndexerRequest request)
            => FetchIndexerResponse(request);
    }
}
