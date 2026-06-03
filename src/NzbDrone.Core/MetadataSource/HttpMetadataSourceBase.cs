using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers.Http;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Abstract HTTP-backed metadata source. SIBLING (not subclass) of the former
    /// <c>HttpAggregatorBase</c> per RESEARCH §Open Question 1 — duplicates
    /// the SourceKey + UA injection logic to keep the IIndexer and IMetadataSource
    /// auto-discovery families cleanly separate. (Phase 39 RETIRE-02: <c>HttpAggregatorBase</c>
    /// itself was deleted; this base survives independently.)
    ///
    /// Honest UA + per-instance override per Phase 1 D-13/D-14. The MangaDex provider
    /// MUST NOT expose <c>UserAgentOverride</c> to UI per CONTEXT MangaDex constraints
    /// (omit <c>[FieldDefinition]</c> on the property in MangaDexMetadataSourceSettings).
    /// </summary>
    public abstract class HttpMetadataSourceBase<TSettings> : MetadataSourceBase<TSettings>
        where TSettings : class, IProviderConfig, IHttpAggregatorSettings, new()
    {
        protected readonly IHttpClient _httpClient;

        protected HttpMetadataSourceBase(IHttpClient httpClient, Logger logger)
            : base(logger)
        {
            _httpClient = httpClient;
        }

        protected string SourceKey => string.IsNullOrWhiteSpace(Settings.SourceKey)
            ? DefaultSourceKey
            : Settings.SourceKey;

        /// <summary>
        /// Honest User-Agent format per D-13: hard-coded <c>"Mangarr/"</c> prefix + assembly
        /// version (major.minor). Does NOT use <see cref="BuildInfo.AppName"/> which still
        /// reads "Mangarr" until Phase 8 rebrand.
        /// </summary>
        protected virtual string BuildUserAgent()
        {
            return $"Mangarr/{BuildInfo.Version.ToString(2)}";
        }

        /// <summary>
        /// Resolve the User-Agent for the next request. Per D-14 the user may override the
        /// honest default per-instance via
        /// <see cref="IHttpAggregatorSettings.UserAgentOverride"/>.
        /// </summary>
        protected string ResolveUserAgent()
        {
            return string.IsNullOrWhiteSpace(Settings.UserAgentOverride)
                ? BuildUserAgent()
                : Settings.UserAgentOverride;
        }

        /// <summary>
        /// Configure an <see cref="HttpRequest"/> with our SourceKey rate-limit budget +
        /// honest UA + JSON Accept header before dispatch. Concrete providers (MangaDex /
        /// AniList / MAL) call this on every outbound request.
        /// </summary>
        protected HttpRequest BuildRequest(string url)
        {
            var req = new HttpRequest(url);
            req.RateLimitKey = SourceKey;
            req.Headers["User-Agent"] = ResolveUserAgent();
            req.Headers["Accept"] = "application/json";
            return req;
        }
    }
}
