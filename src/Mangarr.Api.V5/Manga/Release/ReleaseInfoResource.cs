using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;

namespace Mangarr.Api.V5.Manga.Release
{
    // Sonarr divergence: NEW manga sibling per debug session interactive-search-rejections (2026-05-08).
    // Role-match analog: src/Sonarr.Api.V5/Release/ReleaseInfoResource.cs (deleted by Phase 15-10
    // commit d0b67fdf3 along with the rest of the V5 TV Release/ directory).
    //
    // Ports the canonical Sonarr V5 nested ReleaseInfo wire shape so the frontend
    // useReleases.ts `ReleaseInfo` interface (frontend/src/InteractiveSearch/useReleases.ts:77-98)
    // — which the row destructures off `release.release.{guid, age, title, indexerId, ...}` —
    // resolves to a populated object instead of `undefined`. Mirrors upstream Sonarr's
    // ReleaseInfoResource verbatim except: NO TvdbId / TvRageId / ImdbId (manga has no TVDB
    // crosslink); manga rows pass 0 / null defaults to satisfy the frontend's non-optional fields.
    //
    // Phase 8 cleanup: collapse with the unified ReleaseInfoResource when Tv/ deletes.
    public class ReleaseInfoResource
    {
        public string? Guid { get; set; }
        public int Age { get; set; }
        public double AgeHours { get; set; }
        public double AgeMinutes { get; set; }
        public long Size { get; set; }
        public int IndexerId { get; set; }
        public string? Indexer { get; set; }

        // GWIX: the gateway aggregates many upstream sources under one indexer identity. `Indexer`
        // is the gateway's display name (stamped by CleanupReleases); `Source` is the per-release
        // upstream SourceKey (mangadex/comix/…) the GatewayParser carries on ReleaseInfo.Source.
        // The InteractiveSearch row shows it under the indexer name. Null for non-gateway releases.
        public string? Source { get; set; }

        // Per-release vote count carried from ReleaseInfo.Votes (mirrors how Source is carried).
        // The frontend reads it off `release.release.votes` for the InteractiveSearch Votes column.
        public int Votes { get; set; }
        public string? Title { get; set; }

        // Frontend interface (useReleases.ts:86-87) declares these as `number` — preserve the
        // shape but always emit 0 for manga (no TVDB / TvRage crosslink for manga releases).
        public int TvdbId { get; set; }
        public int TvRageId { get; set; }

        public DateTime PublishDate { get; set; }
        public string? CommentUrl { get; set; }
        public string? DownloadUrl { get; set; }
        public string? InfoUrl { get; set; }
        public string? MagnetUrl { get; set; }
        public string? InfoHash { get; set; }
        public int? Seeders { get; set; }
        public int? Leechers { get; set; }
        public DownloadProtocol Protocol { get; set; }
        public int IndexerFlags { get; set; }
    }

    public static class ReleaseInfoResourceMapper
    {
        public static ReleaseInfoResource ToResource(this ReleaseInfo releaseInfo)
        {
            // Manga indexers do not currently produce TorrentInfo (no torrent manga indexer in v1
            // per Phase 4 D-10; only the InProcessImageDownloadClient / Http protocol). The cast
            // returns null for non-torrent ReleaseInfo so seeders/leechers/magnet stay null.
            // IndexerFlags lives on the base ReleaseInfo (not TorrentInfo), so it is read directly
            // off `releaseInfo` and cast int per the upstream Sonarr V5 contract.
            var torrentInfo = releaseInfo as TorrentInfo;

            return new ReleaseInfoResource
            {
                Guid = releaseInfo.Guid,
                Age = releaseInfo.Age,
                AgeHours = releaseInfo.AgeHours,
                AgeMinutes = releaseInfo.AgeMinutes,
                Size = releaseInfo.Size,
                IndexerId = releaseInfo.IndexerId,
                Indexer = releaseInfo.Indexer,
                Source = releaseInfo.Source,
                Votes = releaseInfo.Votes,
                Title = releaseInfo.Title,
                TvdbId = 0,
                TvRageId = 0,
                PublishDate = releaseInfo.PublishDate,
                CommentUrl = releaseInfo.CommentUrl,
                DownloadUrl = releaseInfo.DownloadUrl,
                InfoUrl = releaseInfo.InfoUrl,
                MagnetUrl = torrentInfo?.MagnetUrl,
                InfoHash = torrentInfo?.InfoHash,
                Seeders = torrentInfo?.Seeders,
                Leechers = (torrentInfo != null && torrentInfo.Peers.HasValue && torrentInfo.Seeders.HasValue)
                    ? (torrentInfo.Peers.Value - torrentInfo.Seeders.Value)
                    : (int?)null,
                Protocol = releaseInfo.DownloadProtocol,
                IndexerFlags = (int)releaseInfo.IndexerFlags,
            };
        }
    }
}
