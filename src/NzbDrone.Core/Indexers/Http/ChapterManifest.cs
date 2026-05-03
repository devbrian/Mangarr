using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Indexers.Http
{
    /// <summary>
    /// Phase 4 D-02 — chapter manifest emitted by an indexer's
    /// <see cref="HttpAggregatorBase{TSettings}.GetChapterPages"/>. Init-only properties
    /// (immutable after construction).
    ///
    /// Premature <c>IAsyncEnumerable&lt;&gt;</c> rejected per D-02 — neither v1 source paginates
    /// page enumeration; both return the full list in a single response.
    /// </summary>
    public sealed class ChapterManifest
    {
        /// <summary>Resolved per-page descriptors, in display order (PageIndex 1-based).</summary>
        public IReadOnlyList<ChapterPage> Pages { get; init; } = Array.Empty<ChapterPage>();

        /// <summary>
        /// Scanlation group (translator credit) — flows into ComicInfo &lt;Translator&gt; +
        /// &lt;ScanInformation&gt; (Phase 4 archiver path; consumed by plan 04-04).
        /// </summary>
        public string ScanlationGroup { get; init; }

        /// <summary>Total number of pages reported by the source.</summary>
        public int TotalCount { get; init; }

        /// <summary>
        /// Token expiry hint (informational only; D-03 reactive 403/410 → re-fetch is canonical).
        /// MangaDex sets this to <c>UtcNow + 15min</c>; durable-URL sources (e.g. comix.to)
        /// leave it null.
        /// </summary>
        public DateTimeOffset? ExpiresAt { get; init; }
    }
}
