using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Download.History.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 36 (LOOP-02) — see DIVERGENCE.md.
    // Provenance: v5-develop:src/NzbDrone.Core/Download/History/DownloadHistory.cs.
    // Role-match analog (exact sibling-entity shape): src/NzbDrone.Core/History/Manga/ChapterHistory.cs
    //   (ModelBase, Data = new Dictionary<string,string>() in ctor, EventType enum).
    //
    // TWO-SURFACE NOTE (load-bearing): this is the LEAN matching join, NOT the rich user-facing
    // ChapterHistory. Sonarr deliberately keeps two surfaces — a retention-swept user history
    // (ChapterHistory) AND a never-swept DownloadId-keyed join (this one) so the LOOP-02 matcher can
    // resolve manga + chapters from a DownloadId immediately after a grab without a title-parse
    // fallback. Do NOT reuse ChapterHistory.FindByDownloadId; query MangaDownloadHistory instead.
    //
    // ChapterIds (List<int>) and Data (Dictionary<string,string>) round-trip as JSON via the globally
    // registered EmbeddedDocumentConverter<List<int>> / <Dictionary<string,string>> (TableMapping.cs).
    // ChapterIds is a JSON array — a multi-chapter grab resolves ALL chapter ids (Pitfall 2), never
    // collapsing a pack to a single chapter.
    public class MangaDownloadHistory : ModelBase
    {
        public MangaDownloadHistory()
        {
            ChapterIds = new List<int>();
            Data = new Dictionary<string, string>();
        }

        public string DownloadId { get; set; }
        public int MangaId { get; set; }
        public List<int> ChapterIds { get; set; }
        public MangaDownloadHistoryEventType EventType { get; set; }
        public string SourceTitle { get; set; }
        public DateTime Date { get; set; }
        public Dictionary<string, string> Data { get; set; }
    }
}
