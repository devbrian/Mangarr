using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 16 STRUCT-02 — see DIVERGENCE.md.
    // Captures the conscious manga-domain divergence from Sonarr's Episode shape:
    // multilingual scanlations exist for manga but not for TV. Per Phase 16
    // CONTEXT D-01 (stale-release retention) + D-02 (FirstReleaseDate denormalization),
    // this entity carries per-(language, group) translation upload metadata while
    // the canonical Chapter row stays language-free at the (MangaId, ChapterNumber) grain.
    // Role-match analog: src/NzbDrone.Core/Manga/Chapter.cs (sibling on same ModelBase).
    public class ChapterRelease : ModelBase
    {
        public int ChapterId { get; set; }                    // soft FK to Chapters.Id; cascade in C#
        public string TranslatedLanguage { get; set; }        // BCP-47, NotNullable
        public string ScanlationGroup { get; set; }           // Nullable
        public DateTime? ReleaseDate { get; set; }            // per-translation upload time (NOT chapter-publish date — D-02)
        public string ExternalId { get; set; }                // Nullable (e.g. MangaDex chapter UUID)
    }
}
