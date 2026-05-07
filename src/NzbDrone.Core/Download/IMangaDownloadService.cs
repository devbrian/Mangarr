using System.Threading.Tasks;
using NzbDrone.Core.Parser.Manga.Model;

namespace NzbDrone.Core.Download
{
    // Sonarr divergence: NEW manga sibling per Phase 15 Wave (A) pre-land per
    // .planning/phases/15-domain-rename-rebrand/15-CONTRACTS-AUDIT.md §8 (the single
    // NEW contract). Replaces the Phase 8 RemoteChapter.ToRemoteEpisodeShim() bridge
    // by giving the manga decision pipeline a manga-shaped IDownloadService peer.
    //
    // Role-match analog: src/NzbDrone.Core/Download/IDownloadService.cs.
    //
    // Wave (A) is purely additive on Mangarr-v0 — both IDownloadService (TV) and
    // IMangaDownloadService (manga) coexist; Phase 15 Wave (C) deletes the TV peer
    // when Tv/ deletes.
    public interface IMangaDownloadService
    {
        Task DownloadReport(RemoteChapter remoteChapter, int? downloadClientId);
    }
}
