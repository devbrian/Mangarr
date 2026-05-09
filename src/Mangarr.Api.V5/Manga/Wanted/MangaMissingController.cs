using Mangarr.Api.V5.Manga.Chapter;
using Mangarr.Api.V5.Manga.Subresources;
using Mangarr.Http;
using Mangarr.Http.Extensions;
using Mangarr.Http.REST;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.MediaFiles.MangaImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.SignalR;

namespace Mangarr.Api.V5.Manga.Wanted
{
    // Sonarr divergence: NEW manga V5 controller per Phase 6 Plan 06-09 — see DIVERGENCE.md.
    // Phase-12 follow-up (F-MISSING-SIGNALR closure, 2026-05-06) refactored the base class from
    // plain `Controller` → `RestControllerWithSignalR<MissingChapterResource, Chapter>` and
    // added IHandle subscriptions so the `/manga/wanted/missing` page auto-refreshes on
    // chapter/chapterfile pipeline events end-to-end (matching TV's `/wanted/missing` behavior
    // and mirroring the F-CUTOFF-SIGNALR closure verbatim — see MangaCutoffController.cs).
    //
    // Phase-12 follow-up (canonical-resource-reuse, 2026-05-06) refactored the resource shape
    // from custom `MissingChapterResource` (deleted) → canonical `ChapterResource` reuse,
    // mirroring TV's `MissingController` returning `Ok<PagingResource<EpisodeResource>>` rather
    // than its own custom resource. The query param `bool includeManga` was likewise replaced
    // with TV's `[FromQuery] MangaMissingSubresource[]? includeSubresources` enum-array shape
    // (see `MangaMissingSubresource.cs` — single `Manga` value mirrors TV's `MissingSubresource
    // { Series, Images }`). Closes the canonical-resource-reuse divergence.
    //
    // Role-match analog: src/Mangarr.Api.V5/Wanted/MissingController.cs (TV peer — extends
    // EpisodeControllerWithSignalR which itself extends RestControllerWithSignalR<EpisodeResource,
    // Episode>; manga peer extends RestControllerWithSignalR<,> directly because no
    // ChapterControllerWithSignalR base class exists in v1 — verified via Grep 2026-05-06).
    //
    // WANTED-01..03: paged list of monitored chapters that have no ChapterFile imported,
    // filterable by mangaIds + languages + ageRating.
    //
    // Manga sibling preserves: [V5ApiController] route attribute (literal string per Plan 07-02
    // URL-shaped key contract); PagingRequestResource shape; monitored filter; FilterExpressions
    // chain.
    //
    // Manga sibling diverges from MissingController:
    //   * Inject IChapterService (paged ChaptersWithoutFiles overload added by Plan 06-09)
    //     + IMangaService (for ageRating filter join + Manga subresource hydration).
    //   * Drop EpisodesWithoutFiles + includeSpecials (manga has no specials concept).
    //   * Add mangaIds + languages + ageRating filters per WANTED-03.
    //   * D-04 honored: IsSynthetic=true rows are surfaced identically to IsSynthetic=false
    //     rows by default. No `WHERE IsSynthetic = false` filter is applied. A future
    //     `excludeSynthetic` query param could opt-in to the filter; v1 default is INCLUDE.
    //   * ageRating filter is post-paged (in-memory) because it requires a JOIN to Manga
    //     and the v1 ChapterRepository.ChaptersWithoutFiles does not JOIN. Acceptable for
    //     Phase 6 — the result set is bounded by the paging spec (default 10/page).
    //   * Extends RestControllerWithSignalR<ChapterResource, Chapter> + subscribes to
    //     ChapterGrabbedEvent / ChapterImportedEvent / ChapterFileDeletedEvent (NOT plain
    //     Controller). F-MISSING-SIGNALR follow-up (2026-05-06) — mirrors the F-CUTOFF-SIGNALR
    //     closure for cross-controller consistency between the two manga V5 wanted endpoints.
    //
    // SignalR emission contract (mirrors TV EpisodeControllerWithSignalR shape verbatim):
    //   * `manga/wanted/missing` resource name auto-derives from
    //     [V5ApiController("manga/wanted/missing")] via RestControllerWithSignalR.cs:23-33.
    //   * IHandle<ChapterGrabbedEvent> → broadcasts ModelAction.Updated for each chapter id in the
    //     event's RemoteChapter.Chapters list (mirrors TV EpisodeGrabbedEvent fan-out at
    //     EpisodeControllerWithSignalR.cs:122-132).
    //   * IHandle<ChapterImportedEvent> → broadcasts ModelAction.Updated for the imported Chapter's
    //     Id (mirrors TV EpisodeImportedEvent fan-out at EpisodeControllerWithSignalR.cs:134-141 —
    //     manga ChapterImportedEvent carries a single Chapter, not a list, because the manga import
    //     pipeline imports one chapter per event per Plan 06-07 Pitfall 4).
    //   * IHandle<ChapterFileDeletedEvent> → broadcasts ModelAction.Updated for the deleted file's
    //     ChapterId (mirrors TV EpisodeFileDeletedEvent fan-out at EpisodeControllerWithSignalR.cs:143-150).
    //   * Upgrade-reason short-circuit on ChapterFileDeletedEvent mirrors MangaController's
    //     ChapterFileDeletedEvent handler (MangaController.cs:336-341) — the upcoming Add event
    //     fires next, so broadcasting twice for one logical change is wasteful.
    //
    // Phase 8 cleanup: collapse with MissingController when Tv/ deletes.
    [V5ApiController("manga/wanted/missing")]
    public class MangaMissingController : RestControllerWithSignalR<ChapterResource, NzbDrone.Core.Manga.Chapter>,
                                             IHandle<ChapterGrabbedEvent>,
                                             IHandle<ChapterImportedEvent>,
                                             IHandle<ChapterFileDeletedEvent>
    {
        private readonly IChapterService _chapterService;
        private readonly IMangaService _mangaService;
        private readonly IChapterReleaseService _chapterReleaseService;

        public MangaMissingController(IChapterService chapterService,
                                         IMangaService mangaService,
                                         IChapterReleaseService chapterReleaseService,
                                         IBroadcastSignalRMessage signalRBroadcaster)
            : base(signalRBroadcaster)
        {
            _chapterService = chapterService;
            _mangaService = mangaService;
            _chapterReleaseService = chapterReleaseService;
        }

        [HttpGet]
        [Produces("application/json")]
        public Ok<PagingResource<ChapterResource>> GetMissingChapters([FromQuery] PagingRequestResource paging,
                                                                      bool monitored = true,
                                                                      [FromQuery] int[]? mangaIds = null,
                                                                      [FromQuery] string[]? languages = null,
                                                                      [FromQuery] string? ageRating = null,
                                                                      [FromQuery] MangaMissingSubresource[]? includeSubresources = null)
        {
            var includeManga = includeSubresources?.Contains(MangaMissingSubresource.Manga) ?? false;

            // Sonarr divergence: Phase 16 STRUCT-04 — Chapter.ReleaseDate was lifted to
            // ChapterRelease.ReleaseDate (per-translation upload time) and replaced with
            // Chapter.FirstReleaseDate (Sonarr-mirror of Episode.AirDateUtc — the upstream
            // chapter-publish date). The server-side allowed-sort-key set + default sort key
            // accept the new column name. Frontend continues to send `?sortKey=releaseDate`
            // until Plan 16-06 retires that reference; "releaseDate" falls outside the allowed
            // set so the controller defaults to "firstReleaseDate" (matches the SQL column).
            var pagingResource = new PagingResource<ChapterResource>(paging);
            var pagingSpec = pagingResource.MapToPagingSpec<ChapterResource, NzbDrone.Core.Manga.Chapter>(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "firstReleaseDate",
                    "chapterNumber",
                    "title"
                },
                "firstReleaseDate",
                SortDirection.Ascending);

            if (monitored)
            {
                pagingSpec.FilterExpressions.Add(c => c.Monitored == true);
            }

            if (mangaIds != null && mangaIds.Length > 0)
            {
                pagingSpec.FilterExpressions.Add(c => mangaIds.Contains(c.MangaId));
            }

            // D-04 (zero-release-Missing) note on the unfiltered shape: Chapters with zero
            // ChapterRelease rows ARE in the missing list — Wanted/Missing alias-flips to
            // "Chapter without any ChapterRelease rows OR Chapter with releases but no file."
            // The languages[] filter narrows the set to Chapters with at least one matching
            // ChapterRelease — zero-release chapters are EXCLUDED when the filter is set
            // (they have no language to match). Pre-Phase-16 the same gate read
            // Chapter.TranslatedLanguage; STRUCT-04 lifted that to ChapterRelease.

            // ageRating + languages both require a JOIN and apply post-paged in-memory.
            // The result set is bounded by the paging spec (default 10/page) so the
            // in-memory pass cost is negligible. Future Phase 8 collapse may push the
            // filter into the SQL builder.
            var page = pagingSpec.ApplyToPage(
                spec => _chapterService.ChaptersWithoutFiles(spec),
                c => MapToResource(c, includeManga));

            // Sonarr divergence: Phase 16 STRUCT-06 — languages[] filter retargets at
            // ChapterRelease.TranslatedLanguage. Pre-Phase-16 the gate read
            // Chapter.TranslatedLanguage (canonical-grain) which is dropped per STRUCT-04.
            // Post-Phase-16 a chapter is "in" the filter if ANY of its ChapterReleases match
            // any language in the filter (case-insensitive — BCP-47 codes).
            // N+1-safe via IChapterReleaseService.GetReleasesByChapterIds (single bulk SQL).
            if (languages != null && languages.Length > 0)
            {
                var chapterIdsInPage = page.Records.Select(r => r.Id).ToList();
                var releasesByChapter = _chapterReleaseService
                    .GetReleasesByChapterIds(chapterIdsInPage)
                    .GroupBy(r => r.ChapterId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(r => r.TranslatedLanguage).ToList());

                var langFilter = new HashSet<string>(languages, StringComparer.OrdinalIgnoreCase);

                page.Records = page.Records
                    .Where(r =>
                    {
                        // D-04: zero-release chapters EXCLUDED when filter is set — no language to match.
                        if (!releasesByChapter.TryGetValue(r.Id, out var langs))
                        {
                            return false;
                        }

                        return langs.Any(l => l != null && langFilter.Contains(l));
                    })
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(ageRating))
            {
                var mangaIdsInPage = page.Records.Select(r => r.MangaId).Distinct().ToList();
                var mangaLookup = _mangaService.GetManga(mangaIdsInPage)
                    .ToDictionary(m => m.Id, m => m.ContentRating);
                page.Records = page.Records
                    .Where(r => mangaLookup.TryGetValue(r.MangaId, out var rating)
                                && string.Equals(rating, ageRating, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            return TypedResults.Ok(page);
        }

        // RestControllerWithSignalR.BroadcastResourceChange(ModelAction, int) round-trips through
        // GetResourceById to materialize the resource for the SignalR Body. Default base impl
        // throws — provide the manga-shape lookup so IHandle subscribers can broadcast by id.
        // Mirrors EpisodeControllerWithSignalR.GetResourceById at EpisodeControllerWithSignalR.cs:53-58
        // and MangaCutoffController.GetResourceById (F-CUTOFF-SIGNALR sibling, 2026-05-06).
        // After canonical-resource-reuse follow-up (2026-05-06): returns ChapterResource via the
        // canonical ChapterResourceMapper.ToResource extension (no Manga subresource hydration on
        // the broadcast path — TV's EpisodeControllerWithSignalR.GetResourceById hydrates Series
        // for the broadcast, but the manga-side default is `false` for parity with the original
        // shape; the broadcast Body's Id is the load-bearing field for updatePagedItem<Episode>).
        protected override ChapterResource GetResourceById(int id)
        {
            var chapter = _chapterService.GetChapter(id);
            return MapToResource(chapter, includeManga: false);
        }

        private ChapterResource MapToResource(NzbDrone.Core.Manga.Chapter chapter, bool includeManga)
        {
            var resource = chapter.ToResource();

            if (includeManga)
            {
                var manga = _mangaService.GetManga(chapter.MangaId);
                if (manga != null)
                {
                    resource.Manga = new MangaSubresource
                    {
                        Id = manga.Id,
                        Title = manga.Title
                    };
                }
            }

            return resource;
        }

        // F-MISSING-SIGNALR follow-up (2026-05-06): mirrors TV
        // EpisodeControllerWithSignalR.Handle(EpisodeGrabbedEvent) at lines 122-132 +
        // MangaCutoffController.Handle(ChapterGrabbedEvent) sibling. RemoteChapter carries the
        // resolved Chapters list (Parser/Manga/Model/RemoteChapter.cs:32). Each id round-trips
        // through BroadcastResourceChange → GetResourceById → ChapterResource so the
        // React Query cache for ['/manga/wanted/missing'] sees a fresh row per chapter on grab.
        [NonAction]
        public void Handle(ChapterGrabbedEvent message)
        {
            foreach (var chapter in message.RemoteChapter.Chapters)
            {
                BroadcastResourceChange(ModelAction.Updated, chapter.Id);
            }
        }

        // F-MISSING-SIGNALR follow-up (2026-05-06): mirrors TV
        // EpisodeControllerWithSignalR.Handle(EpisodeImportedEvent) at lines 134-141 +
        // MangaCutoffController.Handle(ChapterImportedEvent) sibling. Manga ChapterImportedEvent
        // carries a single Chapter (NOT a list) — the manga import pipeline imports one chapter
        // per event per Plan 06-07 Pitfall 4 (publish AFTER ChapterFile DB commit + filesystem
        // move complete).
        [NonAction]
        public void Handle(ChapterImportedEvent message)
        {
            BroadcastResourceChange(ModelAction.Updated, message.Chapter.Id);
        }

        // F-MISSING-SIGNALR follow-up (2026-05-06): mirrors TV
        // EpisodeControllerWithSignalR.Handle(EpisodeFileDeletedEvent) at lines 143-150 PLUS the
        // Upgrade-reason short-circuit from MangaController.Handle(ChapterFileDeletedEvent) at
        // MangaController.cs:336-341 (the upcoming Add event fires next, so broadcasting twice
        // for one logical change is wasteful) + MangaCutoffController.Handle(ChapterFileDeletedEvent)
        // sibling. ChapterFile carries a single ChapterId.
        [NonAction]
        public void Handle(ChapterFileDeletedEvent message)
        {
            if (message.Reason == NzbDrone.Core.MediaFiles.DeleteMediaFileReason.Upgrade)
            {
                return;
            }

            BroadcastResourceChange(ModelAction.Updated, message.ChapterFile.ChapterId);
        }
    }
}
