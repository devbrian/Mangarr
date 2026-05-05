using System;
using System.Collections.Generic;
using NzbDrone.Core.DecisionEngine.Manga;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Queue.Manga;

namespace NzbDrone.Core.Download.Pending.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 9 D-09-06 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Download/Pending/IPendingReleaseService.cs
    // (TV interface — 11 methods).
    //
    // 9 public methods (skip TV's 3 *Obsolete — manga has no V3 API per Phase 14 D-12).
    // The implementation lives in MangaPendingReleaseService.cs (Plan 09-10) which also
    // wires 9 IHandle subscribers (8 from D-09-07 + TranslationProfileUpdatedEvent per
    // Open Q §1) and enforces Pitfall 4 ordering on every state-mutating path.
    //
    // Phase 14 cleanup: collapse with IPendingReleaseService when Tv/ deletes.
    public interface IMangaPendingReleaseService
    {
        void Add(MangaDownloadDecision decision, PendingReleaseReason reason);
        void AddMany(List<Tuple<MangaDownloadDecision, PendingReleaseReason>> decisions);

        List<ReleaseInfo> GetPending();
        List<RemoteChapter> GetPendingRemoteChapters(int mangaId);

        List<MangaQueueItem> GetPendingQueue();
        MangaQueueItem FindPendingQueueItem(int queueId);
        void RemovePendingQueueItems(int queueId);

        RemoteChapter OldestPendingRelease(int mangaId, int[] chapterIds);

        // 3 *Obsolete methods OMITTED per D-09-06 — manga has no V3 API (Phase 14 D-12).
    }
}
