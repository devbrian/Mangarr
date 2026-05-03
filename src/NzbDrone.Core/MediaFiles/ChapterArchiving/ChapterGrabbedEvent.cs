using NzbDrone.Common.Messaging;
using NzbDrone.Core.Parser.Manga.Model;

namespace NzbDrone.Core.MediaFiles.ChapterArchiving
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-21 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Download/EpisodeGrabbedEvent.cs.
    // Emitted by Phase 4 InProcessImageDownloadClient after a successful "grab"
    // (state row inserted, manifest fetched). Phase 6 ChapterHistoryService.Handle
    // (Plan 06-03) writes a Grabbed history row in response.
    public class ChapterGrabbedEvent : IEvent
    {
        public RemoteChapter RemoteChapter { get; }
        public string DownloadId { get; }
        public string DownloadClient { get; }

        public ChapterGrabbedEvent(RemoteChapter remoteChapter, string downloadId, string downloadClient)
        {
            RemoteChapter = remoteChapter;
            DownloadId = downloadId;
            DownloadClient = downloadClient;
        }
    }
}
