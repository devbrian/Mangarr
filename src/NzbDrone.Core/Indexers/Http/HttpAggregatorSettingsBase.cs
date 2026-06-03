using System;

namespace NzbDrone.Core.Indexers.Http
{
    /// <summary>
    /// SourceKey settings contract for Mangarr metadata-source plugins (Phase 1 D-11/D-12/D-14).
    /// Extends <see cref="IIndexerSettings"/> with the three Mangarr-aggregator-specific fields.
    ///
    /// Phase 39 (RETIRE-02): the in-process aggregator indexers + their <c>HttpAggregatorBase</c>
    /// were deleted (the gateway is the sole <c>IIndexer</c>). This interface SURVIVES as the
    /// SourceKey contract read by <c>MangaDownloadDecisionMaker</c> (CF custom-format scoring) and
    /// implemented by the three metadata-source settings classes (MangaDex / AniList / MyAnimeList)
    /// via <c>HttpMetadataSourceBase&lt;TSettings&gt;</c>. The dead concrete
    /// <c>HttpAggregatorSettings</c> POCO + <c>HttpAggregatorSettingsValidator</c> were removed with
    /// the indexers (no surviving consumer) — the metadata sources implement this interface directly.
    /// </summary>
    public interface IHttpAggregatorSettings : IIndexerSettings
    {
        /// <summary>
        /// D-12: User-overridable per-source rate-limit bucket key.
        /// </summary>
        string SourceKey { get; set; }

        /// <summary>
        /// D-12: User-overridable rate limit (TimeSpan). Falls back to the source default when null.
        /// Implementers persist this as a numeric seconds value on disk.
        /// </summary>
        TimeSpan? Rate { get; set; }

        /// <summary>
        /// D-14: User-overridable opt-out. Honest <c>Mangarr/{version}</c> applied when blank.
        /// </summary>
        string UserAgentOverride { get; set; }
    }
}
