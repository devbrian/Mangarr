namespace NzbDrone.Core.Indexers.Http
{
    /// <summary>
    /// Phase 4 D-02 — per-page descriptor inside a <see cref="ChapterManifest"/>. Init-only.
    /// </summary>
    public sealed class ChapterPage
    {
        /// <summary>Resolved image URL the in-process downloader will GET.</summary>
        public string Url { get; init; }

        /// <summary>1-based page index (D-15: archiver pads to 4-digit zero-padded filename).</summary>
        public int PageIndex { get; init; }

        /// <summary>
        /// Optional Content-Type hint when the URL has no extension. Null when URL embeds
        /// the extension already (typical for both v1 sources).
        /// </summary>
        public string ContentTypeHint { get; init; }
    }
}
