using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers.Http
{
    /// <summary>
    /// Sonarr divergence: non-generic marker per Phase 4 D-01 — see DIVERGENCE.md (entry added in plan 04-08).
    ///
    /// Phase 4 — non-generic marker for <see cref="HttpAggregatorBase{TSettings}"/> so the
    /// in-process downloader (plan 04-03) can carry a typed aggregator reference through its
    /// <c>Channel&lt;ChapterDownloadJob&gt;</c> pipeline without spelling the generic.
    /// All members are forwarders to the existing <see cref="HttpAggregatorBase{TSettings}"/> surface.
    /// Phase 8 cleanup: this interface either retires (if <c>HttpAggregatorBase</c> loses its generic
    /// after Tv/ deletes) or stays as the canonical non-generic API.
    ///
    /// <para>
    /// Signatures match <see cref="HttpAggregatorBase{TSettings}"/> exactly so the abstract base's
    /// existing methods satisfy the interface contract without re-declaration:
    /// <c>SourceKey</c> (protected → made public via the base) is exposed via the interface; the
    /// dictionary return type is concrete <see cref="Dictionary{TKey,TValue}"/> to match
    /// <c>GetDownloadHeaders</c> verbatim across both source plugins.
    /// </para>
    /// </summary>
    public interface IHttpAggregator
    {
        /// <summary>Effective per-source rate-limit bucket (Phase 1 D-11/D-12).</summary>
        string SourceKey { get; }

        /// <summary>Honest UA per Phase 1 D-13/D-14 — opt-out via Settings.</summary>
        string ResolveUserAgent();

        /// <summary>Per-source headers applied to every image GET (Phase 3 D-14 contract).</summary>
        Dictionary<string, string> GetDownloadHeaders(ReleaseInfo release);

        /// <summary>Phase 4 D-01/D-04 — manifest dereference per source (abstract on the base).</summary>
        Task<ChapterManifest> GetChapterPages(ReleaseInfo release);
    }
}
