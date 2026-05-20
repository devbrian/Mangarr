using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Manga.Events
{
    // POCO event published by MangaService.DeleteManga after Delete. Mirrors Mangarr's
    // SeriesDeletedEvent (Tv/Events/SeriesDeletedEvent.cs) shape.
    //
    // Phase 26 Plan 26-04 (RESEARCH §Q2 / Open Q #1 / Assumption A2) — ADDED
    // `AddImportListExclusion` bool. Default `true` matches Sonarr UX: when a user
    // deletes a manga via the V5 UI, the ImportList substrate auto-adds an exclusion
    // so subsequent ImportList syncs don't re-add it. Callers that want to opt out
    // (admin tooling, system-initiated resyncs, programmatic re-add scenarios) pass
    // `false` explicitly.
    //
    // The two-arg ctor is preserved for backwards-compat — existing producers (the V5
    // MangaController and MangaEditorController, plus 13 test-side instantiations) ride
    // the default-true path verbatim. Only the V5 controller has the option to surface
    // the bool on the request DTO when the Phase 26 UI work lands in Plan 26-05.
    public class MangaDeletedEvent : IEvent
    {
        public Manga Manga { get; private set; }
        public bool DeleteFiles { get; private set; }
        public bool AddImportListExclusion { get; private set; }

        public MangaDeletedEvent(Manga manga, bool deleteFiles, bool addImportListExclusion = true)
        {
            Manga = manga;
            DeleteFiles = deleteFiles;
            AddImportListExclusion = addImportListExclusion;
        }
    }
}
