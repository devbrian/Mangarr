using System.Collections.Generic;

namespace NzbDrone.Core.Queue.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-20 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Queue/QueueService.cs lines 14–19.
    //
    // Wires the Phase 5 QueueDuplicateSpecification STUB (D-20). Sibling shape
    // mirrors IQueueService verbatim — only the row type is manga-specific.
    // Phase 8 cleanup: collapse with IQueueService when Tv/ deletes.
    public interface IMangaQueueService
    {
        List<MangaQueueItem> GetMangaQueue();
        MangaQueueItem Find(int id);
        void Remove(int id);
    }
}
