using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Http;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser;

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
            IConfigService configService,
            IParsingService parsingService,
            Logger logger,
            ILocalizationService localizationService)
            : base(httpClient, indexerStatusService, configService, parsingService, logger, localizationService)
        {
        }

        public IIndexerRequestGenerator _requestGenerator;

        public override IIndexerRequestGenerator GetRequestGenerator() => _requestGenerator;

        public IParseIndexerResponse _parser;

        public override IParseIndexerResponse GetParser() => _parser;

        /// <summary>
        /// Public passthrough to the protected <see cref="HttpAggregatorBase{TSettings}.FetchIndexerResponse"/>
        /// hook for unit-test assertions.
        /// </summary>
        public Task<IndexerResponse> InvokeFetchIndexerResponse(IndexerRequest request)
            => FetchIndexerResponse(request);
    }
}
