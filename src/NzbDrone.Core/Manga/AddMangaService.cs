using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Manga.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.MetadataSource.MangaDex;
using NzbDrone.Core.MetadataSource.MyAnimeList;
using NzbDrone.Core.Parser.Manga;

namespace NzbDrone.Core.Manga
{
    /// <summary>
    /// META-02 AddManga orchestration mirroring <see cref="NzbDrone.Core.Tv.AddSeriesService"/>.
    ///
    /// <para>Pipeline:
    /// 1. Reject duplicates by any populated cross-source ID
    /// 2. Resolve primary metadata via dynamic primary (D-15) — populates fields + may
    ///    surface MangaDex links.al/links.mal cross-source IDs
    /// 2.5. PER D-19: validate every cross-source ID populated from the primary's links
    ///    field by fetching the linked entity from the secondary provider and re-running
    ///    the full D-21 gate. On failure, RESET the ID to null and emit warning log
    ///    "D-19 validation failed".
    /// 3. PER D-20..D-22: cross-source ID resolution. Includes the explicit reverse-MangaDex
    ///    branch — when primary != MangaDex AND MangaDexId is null, run
    ///    <c>MangaDex.SearchForNewManga(title)</c> and pick the highest-similarity
    ///    D-21-passing hit per D-20. Generic fallback fills remaining missing IDs from any
    ///    other secondary that exposes a search-by-title surface.
    /// 4. Compute clean/sort titles via <see cref="MangaTitleNormalizer.Normalize"/> (D-05)
    /// 5. Persist via <see cref="IMangaService.AddManga"/> (which publishes
    ///    <see cref="Events.MangaAddedEvent"/>)
    /// 6. Synthesize / sync chapters via <see cref="IChapterListService"/> (D-17)
    /// 7. Schedule initial <see cref="RefreshMangaCommand"/> with IsNewManga=true.
    /// </para>
    /// </summary>
    public class AddMangaService : IAddMangaService
    {
        private readonly IMangaService _mangaService;
        private readonly IMetadataSourceFactory _metaFactory;
        private readonly IChapterListService _chapterListService;
        private readonly CrossSourceIdResolver _resolver;
        private readonly IManageCommandQueue _commandQueue;
        private readonly Logger _logger;

        public AddMangaService(IMangaService mangaService,
                               IMetadataSourceFactory metaFactory,
                               IChapterListService chapterListService,
                               CrossSourceIdResolver resolver,
                               IManageCommandQueue commandQueue,
                               Logger logger)
        {
            _mangaService = mangaService;
            _metaFactory = metaFactory;
            _chapterListService = chapterListService;
            _resolver = resolver;
            _commandQueue = commandQueue;
            _logger = logger;
        }

        public Manga AddManga(Manga newManga)
        {
            if (newManga == null)
            {
                throw new ArgumentNullException(nameof(newManga));
            }

            // 1. Reject duplicates by any populated cross-source ID.
            if (newManga.MangaDexId.HasValue && _mangaService.FindByMangaDexId(newManga.MangaDexId.Value) != null)
            {
                throw new InvalidOperationException($"Manga with MangaDex ID {newManga.MangaDexId} already exists");
            }

            if (newManga.MalId.HasValue && _mangaService.FindByMalId(newManga.MalId.Value) != null)
            {
                throw new InvalidOperationException($"Manga with MAL ID {newManga.MalId} already exists");
            }

            if (newManga.AniListId.HasValue && _mangaService.FindByAniListId(newManga.AniListId.Value) != null)
            {
                throw new InvalidOperationException($"Manga with AniList ID {newManga.AniListId} already exists");
            }

            // 2. Resolve primary metadata.
            var primaryDef = _metaFactory.GetPrimary();
            var primary = (IProvideMangaInfo)_metaFactory.GetInstance(primaryDef);
            var primarySourceId = ResolveSourceIdForPrimary(newManga, primary);
            var primaryTuple = primary.GetMangaInfo(primarySourceId);
            var primaryManga = primaryTuple.Item1;
            var primaryChapters = primaryTuple.Item2;
            newManga.ApplyChanges(primaryManga);

            // Carry over the primary IDs returned by GetMangaInfo (incl. any links extracted by
            // MapManga, e.g. MangaDex links.al/mal).
            newManga.MangaDexId ??= primaryManga.MangaDexId;
            newManga.MalId ??= primaryManga.MalId;
            newManga.AniListId ??= primaryManga.AniListId;

            // 2.5. PER D-19: VALIDATE every cross-source ID populated from a primary.links field by
            //      fetching the linked entity from the corresponding secondary provider and confirming
            //      title similarity >=0.85 against MangaTitleNormalizer-normalized candidates (full D-21
            //      gate including multi-axis confirm). If validation FAILS, reset that ID to null and
            //      log "D-19 validation failed" so the cross-source-resolver fallback (step 3) re-runs
            //      via fuzzy title-search instead of trusting the unvalidated link.
            ValidateLinkedCrossSourceIds(newManga, primaryManga);

            // 3. Cross-source ID resolution per D-19..D-22 (best-effort; failures stay null per D-23).
            //    Symmetric per D-20: when primary is NOT MangaDex AND MangaDexId is missing, run
            //    fuzzy title match against MangaDex /manga?title=... and pick the highest-similarity
            //    result clearing the D-21 gate (handled inside ResolveCrossSourceIds).
            ResolveCrossSourceIds(newManga, primaryManga);

            // 4. Compute clean/sort titles via the single normalizer (D-05).
            newManga.CleanTitle = MangaTitleNormalizer.Normalize(newManga.Title);
            newManga.SortTitle = newManga.CleanTitle;
            newManga.Added = DateTime.UtcNow;

            // 5. Persist + publish (MangaService.AddManga publishes MangaAddedEvent).
            var added = _mangaService.AddManga(newManga);

            // 6. Synthesize / sync chapters per D-17.
            _chapterListService.SyncChapters(added, primaryChapters);

            // 7. Schedule initial refresh (IsNewManga=true).
            _commandQueue.Push(new RefreshMangaCommand(new List<int> { added.Id }, isNewManga: true));

            return added;
        }

        private static string ResolveSourceIdForPrimary(Manga manga, IProvideMangaInfo primary)
            => primary switch
            {
                MangaDexMetadataSource _ => manga.MangaDexId?.ToString(),
                AniListMetadataSource _ => manga.AniListId?.ToString(),
                MyAnimeListMetadataSource _ => manga.MalId?.ToString(),
                _ => throw new InvalidOperationException(
                    $"Unknown primary metadata source type {primary?.GetType().Name}")
            }
            ?? throw new ArgumentException(
                $"newManga has no source ID for active primary {primary.GetType().Name}");

        // PER D-19: For every cross-source ID that came from the primary's links field
        // (e.g. MangaDex attributes.links.al/mal), fetch the linked entity from the secondary
        // provider and re-run the full D-21 gate. If the link does NOT validate, RESET the ID
        // to null + log "D-19 validation failed" so the fuzzy-fallback path (step 3) takes over.
        private void ValidateLinkedCrossSourceIds(Manga newManga, Manga primaryResult)
        {
            var primaryCandidate = ToCandidate(primaryResult);

            foreach (var def in _metaFactory.All().Where(d => !d.IsPrimary))
            {
                var src = (IProvideMangaInfo)_metaFactory.GetInstance(def);

                // AniList link validation.
                if (newManga.AniListId.HasValue && src is AniListMetadataSource)
                {
                    try
                    {
                        var linkedTuple = src.GetMangaInfo(newManga.AniListId.Value.ToString());
                        var linked = linkedTuple.Item1;
                        if (!_resolver.TryResolve(primaryCandidate, ToCandidate(linked), out var reason))
                        {
                            _logger.Warn("D-19 validation failed for AniListId={0} on manga {1}: {2}",
                                newManga.AniListId, newManga.Title, reason);
                            newManga.AniListId = null;
                        }
                    }
                    catch (MangaNotFoundException)
                    {
                        _logger.Warn("D-19 validation failed for AniListId={0} on manga {1}: not-found at secondary",
                            newManga.AniListId, newManga.Title);
                        newManga.AniListId = null;
                    }
                }

                // MAL link validation.
                if (newManga.MalId.HasValue && src is MyAnimeListMetadataSource)
                {
                    try
                    {
                        var linkedTuple = src.GetMangaInfo(newManga.MalId.Value.ToString());
                        var linked = linkedTuple.Item1;
                        if (!_resolver.TryResolve(primaryCandidate, ToCandidate(linked), out var reason))
                        {
                            _logger.Warn("D-19 validation failed for MalId={0} on manga {1}: {2}",
                                newManga.MalId, newManga.Title, reason);
                            newManga.MalId = null;
                        }
                    }
                    catch (MangaNotFoundException)
                    {
                        _logger.Warn("D-19 validation failed for MalId={0} on manga {1}: not-found at secondary",
                            newManga.MalId, newManga.Title);
                        newManga.MalId = null;
                    }
                }
            }
        }

        private void ResolveCrossSourceIds(Manga newManga, Manga primaryResult)
        {
            // For each missing cross-source ID, query the corresponding secondary's search-by-title
            // (max 3 search-variant calls per D-22) and gate via CrossSourceIdResolver (D-21).
            var primaryCandidate = ToCandidate(primaryResult);

            // PER D-20 - SYMMETRIC REVERSE DIRECTION (explicit branch):
            // When the primary is AniList or MAL (NOT MangaDex) AND MangaDexId is still null,
            // run fuzzy title match against MangaDex via MangaDexMetadataSource.SearchForNewManga(title)
            // (which calls /manga?title=...) and pick the highest-similarity result clearing the D-21
            // gate. If no match clears the gate, MangaDexId stays null and synthesized chapter-list
            // fallback (D-17 strategy 2) kicks in downstream.
            if (!newManga.MangaDexId.HasValue)
            {
                var primaryDef = _metaFactory.GetPrimary();
                if (primaryDef.Name != "MangaDex")
                {
                    var mdDef = _metaFactory.All().FirstOrDefault(d => d.Name == "MangaDex");
                    if (mdDef != null)
                    {
                        var mdSource = (IMetadataSource)_metaFactory.GetInstance(mdDef);
                        var titles = primaryCandidate.AllTitles.Take(3).Where(t => !string.IsNullOrEmpty(t));
                        foreach (var title in titles)
                        {
                            var hits = mdSource.SearchForNewManga(title);
                            // Pick the highest-similarity hit that clears the D-21 gate.
                            var ranked = hits.Select(h => (Hit: h, Cand: ToCandidate(h)))
                                             .Where(t => _resolver.TryResolve(primaryCandidate, t.Cand, out _))
                                             .ToList();
                            if (ranked.Count > 0)
                            {
                                // First passing hit per D-20 (SearchForNewManga returns ordered by relevance).
                                newManga.MangaDexId = ranked[0].Hit.MangaDexId;
                                _logger.Trace("D-20 reverse-MangaDex resolved MangaDexId={0} for primary={1}",
                                    newManga.MangaDexId, primaryDef.Name);
                                break;
                            }
                        }
                    }
                }
            }

            // Generic fallback for the OTHER direction(s) - fill remaining missing IDs by querying each
            // non-primary secondary.
            var secondaries = new List<(string Name, IMetadataSource Source)>();
            foreach (var def in _metaFactory.All().Where(d => !d.IsPrimary))
            {
                secondaries.Add((def.Name, (IMetadataSource)_metaFactory.GetInstance(def)));
            }

            foreach (var (_, src) in secondaries)
            {
                if (newManga.MangaDexId.HasValue && newManga.MalId.HasValue && newManga.AniListId.HasValue)
                {
                    break;   // all 3 IDs filled
                }

                // Try up to 3 search-variant calls per D-22 (English, romaji, native - best-effort).
                var titles = primaryCandidate.AllTitles.Take(3).Where(t => !string.IsNullOrEmpty(t));
                foreach (var title in titles)
                {
                    var hits = src.SearchForNewManga(title);
                    Manga match = null;
                    foreach (var h in hits)
                    {
                        if (_resolver.TryResolve(primaryCandidate, ToCandidate(h), out _))
                        {
                            match = h;
                            break;
                        }
                    }

                    if (match != null)
                    {
                        newManga.MangaDexId ??= match.MangaDexId;
                        newManga.MalId ??= match.MalId;
                        newManga.AniListId ??= match.AniListId;
                        break;
                    }
                }
            }
        }

        private static MangaCandidate ToCandidate(Manga m) => new MangaCandidate
        {
            AllTitles = m?.Title != null ? new List<string> { m.Title } : new List<string>(),
            PublicationYear = m?.PublicationYear,
            PrimaryAuthor = m?.PrimaryAuthor,
            TotalChapterCount = m?.TotalChapterCount,
        };
    }
}
