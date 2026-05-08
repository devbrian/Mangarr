using Mangarr.Api.V5.CustomFormats;
using Mangarr.Http.REST;
using NzbDrone.Core.DecisionEngine.Manga;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Languages;

namespace Mangarr.Api.V5.Manga.Release
{
    // Sonarr divergence: NEW manga V5 resource per Phase 6 Plan 06-09 — see DIVERGENCE.md.
    // Reshaped 2026-05-08 (debug session interactive-search-rejections) from a flat Sonarr-V3-style
    // POCO to the canonical Sonarr V5 nested shape (ParsedInfo + Release + Decision sub-resources)
    // so the frontend useReleases.ts `Release` interface (frontend/src/InteractiveSearch/useReleases.ts:42-60)
    // — which the row destructures off `release.decision.rejections`, `release.parsedInfo.quality`,
    // `release.release.indexerId`, etc. — receives the wire shape it expects. Pre-reshape the row
    // crashed at InteractiveSearchRow.tsx:106 with `Cannot read properties of undefined (reading
    // 'rejections')` because `decision` was undefined on the flat payload.
    //
    // Role-match analog: src/Sonarr.Api.V5/Release/ReleaseResource.cs (deleted by Phase 15-10 commit
    // d0b67fdf3 along with the rest of the V5 TV Release/ directory; this manga peer is the
    // canonical V5 release resource for v1).
    //
    // Manga sibling preserves: PascalCase POCO + RestResource Id + ToResource() static mapper;
    // 30-min cached-RemoteChapter round-trip via top-level (IndexerId, Guid) — the GET search
    // emits both top-level Guid/IndexerId AND nested Release.Guid/Release.IndexerId; the POST
    // grab is keyed off the top-level pair (the frontend grab body only carries `{ guid, indexerId }`
    // per useReleases.ts:503-507 GrabRelease shape, and the controller's GetCacheKey reads them
    // off the top level).
    //
    // Manga sibling diverges from upstream Sonarr ReleaseResource:
    //   * Adds MangaId / ChapterIds / ScanlationGroup / TranslatedLanguage as first-class fields
    //     (Phase 6 Plan 06-09 — manga override surface).
    //   * Languages list synthesizes a single Language from the BCP-47 TranslatedLanguage when
    //     present (frontend reads `release.languages.map(l => l.name)` for filter chips and the
    //     stubbed EpisodeLanguages component; sending [{ Id: 0, Name: "en" }] keeps the field
    //     non-empty for the rare case the row code reads it).
    //   * MappedSeriesId = MangaId (frontend reads `mappedSeriesId` for the override-match modal
    //     payload but the manga grab path resolves via cached RemoteChapter, not this field).
    //   * MappedEpisodeInfo synthesizes one ReleaseEpisode shim per ChapterId (the row's
    //     ReleaseSceneIndicator is a Phase 15 stub — only the destructure of the array matters).
    //   * NO QualityWeight / Quality model logic — the V5 mapper sets QualityWeight = 0 for
    //     manga (manga has no quality profile per Phase 5 D-04).
    //   * SceneMapping always null (manga has no scene numbering per Phase 7 RESEARCH).
    //
    // Phase 8 cleanup: collapse with the unified ReleaseResource when Tv/ deletes.
    public class MangaReleaseResource : RestResource
    {
        // Top-level identity fields preserved for cache-key continuity and frontend grab POST
        // body shape (POST `/manga/release` payload is `{ guid, indexerId }` — these MUST stay
        // top-level so MangaReleaseController.GetCacheKey resolves the cached RemoteChapter).
        public string? Guid { get; set; }
        public int IndexerId { get; set; }

        // Manga-specific top-level fields (override surface).
        public int? MangaId { get; set; }
        public List<int>? ChapterIds { get; set; }
        public string? ScanlationGroup { get; set; }
        public string? TranslatedLanguage { get; set; }

        // Nested wire shape — the three sub-resources the frontend destructures.
        public ParsedChapterInfoResource? ParsedInfo { get; set; }
        public ReleaseInfoResource? Release { get; set; }
        public ReleaseDecisionResource? Decision { get; set; }

        // Top-level fields (mirror upstream Sonarr V5 ReleaseResource).
        public int QualityWeight { get; set; }
        public List<Language> Languages { get; set; } = new List<Language>();
        public int? MappedSeasonNumber { get; set; }
        public int[] MappedEpisodeNumbers { get; set; } = Array.Empty<int>();
        public int[] MappedAbsoluteEpisodeNumbers { get; set; } = Array.Empty<int>();
        public int? MappedSeriesId { get; set; }
        public IEnumerable<ReleaseEpisodeResource> MappedEpisodeInfo { get; set; } = new List<ReleaseEpisodeResource>();
        public bool EpisodeRequested { get; set; }
        public bool DownloadAllowed { get; set; }
        public int ReleaseWeight { get; set; }
        public List<CustomFormatResource>? CustomFormats { get; set; }
        public int CustomFormatScore { get; set; }
        public int IndexerFlags { get; set; }

        // SceneMapping is always null for manga — typed as object? to avoid pulling in the TV
        // AlternateTitleResource (which is Sonarr-V3-shape). Frontend reads `release.sceneMapping`
        // and passes it to the stubbed ReleaseSceneIndicator; null is the safe value.
        public object? SceneMapping { get; set; }

        // Top-level pass-throughs preserved for the rare consumer that reads them off the resource
        // root (Phase 6 sub-wave-B-addition compat — ChapterHistoryResource and MangaQueueResource
        // both still consume the top-level pre-reshape shape via internal mapper paths). Matches
        // the upstream Sonarr V5 ReleaseResource which also kept Title/Indexer/Size/Age top-level
        // alongside the nested Release wrapper.
        public string? Title { get; set; }
        public string? Indexer { get; set; }
        public long Size { get; set; }
        public int Age { get; set; }
        public double AgeHours { get; set; }
        public double AgeMinutes { get; set; }
        public DateTime PublishDate { get; set; }
        public DownloadProtocol Protocol { get; set; }
        public string? CommentUrl { get; set; }
        public string? DownloadUrl { get; set; }
        public string? InfoUrl { get; set; }
    }

    // Frontend ReleaseEpisode shape (frontend/src/InteractiveSearch/useReleases.ts:31-38).
    public class ReleaseEpisodeResource
    {
        public int Id { get; set; }
        public int EpisodeFileId { get; set; }
        public int SeasonNumber { get; set; }
        public int EpisodeNumber { get; set; }
        public int? AbsoluteEpisodeNumber { get; set; }
        public string? Title { get; set; }
    }

    public static class MangaReleaseResourceMapper
    {
        public static MangaReleaseResource ToResource(this MangaDownloadDecision decision, int weight)
        {
            var rc = decision.RemoteChapter;
            var release = rc?.Release;
            var parsed = rc?.ParsedChapterInfo;

            // Synthesize a single Language entry from the BCP-47 TranslatedLanguage so the
            // frontend's Languages-array filter chip and stubbed EpisodeLanguages component receive
            // a non-empty list. Id=0 because the manga BCP-47 string ("en", "ja", ...) does not
            // map to Sonarr's Language enum; Name carries the BCP-47 verbatim.
            var languages = new List<Language>();
            if (!string.IsNullOrWhiteSpace(release?.TranslatedLanguage))
            {
                languages.Add(new Language { Id = 0, Name = release.TranslatedLanguage });
            }

            // Synthesize a ReleaseEpisode shim per ChapterId for MappedEpisodeInfo. The frontend
            // row passes this array to the Override-Match modal; the manga override modal
            // (MangaOverrideMatchModal) doesn't read it — it pulls chapterIds off the top level
            // — but the row does destructure `mappedEpisodeInfo` so the array MUST exist.
            var mappedEpisodeInfo = (rc?.Chapters ?? new List<NzbDrone.Core.Manga.Chapter>())
                .Select(c => new ReleaseEpisodeResource
                {
                    Id = c.Id,
                    EpisodeFileId = c.ChapterFileId ?? 0,
                    SeasonNumber = 0,
                    EpisodeNumber = (int)c.ChapterNumber,
                    AbsoluteEpisodeNumber = c.AbsoluteChapterNumber.HasValue ? (int?)c.AbsoluteChapterNumber.Value : null,
                    Title = c.Title,
                })
                .ToList();

            var customFormats = rc?.CustomFormats?.Select(cf => cf.ToResource(false)).ToList();

            // Rejections-as-strings (legacy flat-shape consumers) are NO LONGER emitted at the
            // top level — the canonical surface is `Decision.Rejections` (structured objects).
            // The MangaReleaseController grab POST receives only `{ guid, indexerId }` from the
            // frontend (useReleases.ts:506) so the receive-side body never carries Rejections.
            return new MangaReleaseResource
            {
                Guid = release?.Guid,
                IndexerId = release?.IndexerId ?? 0,
                MangaId = rc?.Manga?.Id,
                ChapterIds = rc?.Chapters?.Select(c => c.Id).ToList(),
                ScanlationGroup = release?.ScanlationGroup,
                TranslatedLanguage = release?.TranslatedLanguage,

                ParsedInfo = parsed?.ToResource(release?.ScanlationGroup),
                Release = release?.ToResource(),
                Decision = new ReleaseDecisionResource(decision),

                QualityWeight = 0,
                Languages = languages,
                MappedSeasonNumber = 0,
                MappedEpisodeNumbers = mappedEpisodeInfo.Select(e => e.EpisodeNumber).ToArray(),
                MappedAbsoluteEpisodeNumbers = mappedEpisodeInfo
                    .Where(e => e.AbsoluteEpisodeNumber.HasValue)
                    .Select(e => e.AbsoluteEpisodeNumber!.Value)
                    .ToArray(),
                MappedSeriesId = rc?.Manga?.Id,
                MappedEpisodeInfo = mappedEpisodeInfo,
                EpisodeRequested = false,
                DownloadAllowed = true,
                ReleaseWeight = weight,
                CustomFormats = customFormats,
                CustomFormatScore = rc?.CustomFormatScore ?? 0,
                IndexerFlags = (int)(release?.IndexerFlags ?? 0),
                SceneMapping = null,

                // Top-level pass-throughs (compat).
                Title = release?.Title,
                Indexer = release?.Indexer,
                Size = release?.Size ?? 0,
                Age = release?.Age ?? 0,
                AgeHours = release?.AgeHours ?? 0,
                AgeMinutes = release?.AgeMinutes ?? 0,
                PublishDate = release?.PublishDate ?? default,
                Protocol = release?.DownloadProtocol ?? DownloadProtocol.Http,
                CommentUrl = release?.CommentUrl,
                DownloadUrl = release?.DownloadUrl,
                InfoUrl = release?.InfoUrl,
            };
        }
    }
}
