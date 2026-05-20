using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Manga
{
    // Service contract for Manga aggregate. Mirrors Mangarr's ISeriesService
    // (Tv/SeriesService.cs:12-37) shape, with manga-domain divergence:
    //   * FindByTvdbId etc. → FindByMangaDexId / FindByMalId / FindByAniListId
    //   * No IBuildSeriesPaths injection (manga path-build is bespoke; AddMangaService
    //     plan 02-09 owns it)
    //   * No IAutoTaggingService injection (auto-tagging is Phase 5+ territory)
    //   * Publishes MangaAddedEvent / MangaUpdatedEvent / MangaDeletedEvent (not
    //     SeriesEditedEvent — Phase 2 keeps the event model simple)
    public interface IMangaService
    {
        Manga GetManga(int mangaId);
        List<Manga> GetManga(IEnumerable<int> mangaIds);
        Manga AddManga(Manga newManga);
        List<Manga> AddManga(List<Manga> newManga);
        Manga FindByMangaDexId(Guid mangaDexId);
        Manga FindByMalId(int malId);
        Manga FindByAniListId(int aniListId);
        Manga FindByTitle(string title);
        Manga FindByTitle(string title, int year);
        List<Manga> FindByTitleInexact(string title);

        // GH #118 — service-level wrapper for the parser's alt-title resolution path.
        // Strategy 2 of MangaParsingService.GetManga multi-strategy resolution
        // (Sonarr-canonical mirror of ParsingService.GetSeries). Normalizes the
        // input via MangaTitleNormalizer (D-05 single source of truth) then
        // delegates to IMangaRepository.FindByAlternativeTitle. Returns null on
        // no match.
        Manga FindByAlternativeTitle(string title);
        Manga FindByPath(string path);
        void DeleteManga(List<int> mangaIds, bool deleteFiles);

        // Phase 26 Plan 26-04 (D-12 / Open Q #1): 3-arg overload threads
        // `addImportListExclusion` through to MangaDeletedEvent so the V5 controllers
        // can opt out of auto-exclusion (admin tooling, programmatic resyncs). 2-arg
        // overload above delegates here with `addImportListExclusion: true` —
        // Sonarr-canonical UX default.
        void DeleteManga(List<int> mangaIds, bool deleteFiles, bool addImportListExclusion);
        List<Manga> GetAllManga();
        List<Manga> AllForTag(int tagId);
        List<int> AllMangaIds();
        List<Guid> AllMangaDexIds();
        List<int> AllMalIds();
        List<int> AllAniListIds();
        Dictionary<int, string> GetAllMangaPaths();
        Dictionary<int, List<int>> GetAllMangaTags();
        Manga UpdateManga(Manga manga, bool publishUpdatedEvent = true);

        // Phase 10 Plan 10-07 (FINDINGS Open Q 5 close-out): 3-arg overload mirrors
        // TV's ISeriesService.UpdateSeries shape (two-bool gating). UI single-edit PUT
        // path passes both flags true so the MangaController.IHandle<MangaEditedEvent>
        // (Plan 10-05) fires on the user-explicit-edit signal alongside MangaUpdatedEvent;
        // RefreshMangaService can pass both false to suppress events on the metadata-
        // refresh path. The 2-arg overload above stays for backwards-compat — it
        // delegates to this 3-arg overload with triggerSeriesEdited: false.
        Manga UpdateManga(Manga manga, bool publishUpdatedEvent, bool triggerSeriesEdited);

        List<Manga> UpdateManga(List<Manga> manga, bool useExistingRelativeFolder);
        bool MangaPathExists(string folder);
        void RemoveAddOptions(Manga manga);
    }
}
