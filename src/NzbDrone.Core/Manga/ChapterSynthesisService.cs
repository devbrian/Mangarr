using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.DecisionEngine.Manga;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Parser.Manga.Model;

namespace NzbDrone.Core.Manga
{
    // Sonarr divergence: NEW-in-Mangarr reconciliation engine — no TV analog (TheTVDB
    // defines both "what exists" and "what's grabbable"; the external manga gateway
    // exposes chapter releases the MangaDex metadata catalog never enumerated). Makes the
    // local Chapter catalog mirror the gateway so an uncataloged gateway chapter becomes
    // grab-able / wanted instead of silently 200-ing and queueing nothing. See
    // DIVERGENCE.md (Phase 40). Composes 5 proven sub-patterns:
    //   (1) precision-first per-release attribution gate (ID-match-else-EXACT-title;
    //       the inexact substring/fuzzy strategy is EXCLUDED — D-06/D-07);
    //   (2) genuinely-missing WHOLE-number delta against existing rows (D-03);
    //   (3) fractional exclusion on search (D-02);
    //   (4) monitor policy computed directly from Manga.MonitorNewItems, NOT via the
    //       add-time monitor service which reads the add-time MangaMonitor enum (D-05);
    //   (5) write via IChapterListService.SyncChapters passing ONLY absent numbers (D-08).
    public sealed class ChapterSynthesisService : IChapterSynthesisService
    {
        // DoS expansion guard mirroring MangaParser's ~1000-grid-point cap. A remote-
        // controlled maxWhole (the gateway is untrusted upstream) could otherwise drive an
        // unbounded backfill. Clamp + Warn.
        private const int MaxWholeCap = 5000;

        private readonly IChapterListService _chapterListService;
        private readonly IChapterService _chapterService;
        private readonly IMangaService _mangaService;
        private readonly Logger _logger;

        public ChapterSynthesisService(
            IChapterListService chapterListService,
            IChapterService chapterService,
            IMangaService mangaService,
            Logger logger)
        {
            _chapterListService = chapterListService;
            _chapterService = chapterService;
            _mangaService = mangaService;
            _logger = logger;
        }

        public int SynthesizeFromDecisions(Manga manga, List<MangaDownloadDecision> decisions)
        {
            if (manga == null || decisions == null || decisions.Count == 0)
            {
                return 0;
            }

            // (1) Attribution gate per release — NEVER trust the search scope.
            var attributed = decisions
                .Where(d => BelongsToSearchedManga(d, manga))
                .ToList();

            if (attributed.Count == 0)
            {
                return 0;
            }

            // (2)+(3) Collect WHOLE chapter numbers only (D-02 — fractionals enter via on-grab).
            var wholeNumbers = attributed
                .Select(d => d.RemoteChapter?.ParsedChapterInfo)
                .Where(p => p?.ChapterNumbers != null)
                .SelectMany(p => p.ChapterNumbers)
                .Where(n => n == decimal.Truncate(n))
                .ToList();

            // All-fractional edge (CodeRabbit PR #328): if attribution passed but every
            // attributed number is fractional, there is no whole-number backlog to backfill.
            // Bail BEFORE the DefaultIfEmpty(0m) below would force maxWhole=0 and synthesize a
            // phantom chapter 0 (D-02 keeps fractionals out of on-search synthesis entirely).
            if (wholeNumbers.Count == 0)
            {
                return 0;
            }

            var maxWhole = wholeNumbers.Max();

            if (maxWhole > MaxWholeCap)
            {
                _logger.Warn(
                    "Chapter synthesis for manga '{0}' (id={1}) capped maxWhole {2} to {3} (DoS guard).",
                    manga.Title,
                    manga.Id,
                    maxWhole,
                    MaxWholeCap);
                maxWhole = MaxWholeCap;
            }

            // Genuinely-absent delta — only numbers NOT already cataloged reach SyncChapters,
            // so the verbatim Title assignment in ChapterListService can never clobber a real
            // MangaDex title with a synthesized null (D-03/D-08).
            var existingNumbers = _chapterService.GetChaptersByManga(manga.Id)
                .Select(c => c.ChapterNumber)
                .ToHashSet();

            var monitored = ResolveMonitored(manga);

            var rows = new List<Chapter>();

            // Backfill [1..maxWhole] — start at 1, NOT 0. Chapter 0 is NOT a guaranteed member
            // of every manga's chapter list (most series start at chapter 1), so backfilling it
            // unconditionally synthesized a phantom Chapter 0 on every search.
            for (var i = 1; i <= (int)maxWhole; i++)
            {
                var number = (decimal)i;
                if (existingNumbers.Contains(number))
                {
                    continue;
                }

                rows.Add(BuildRow(number, null, null, null, monitored, ChapterType.Regular));
            }

            // Synthesize Chapter 0 ONLY when an actual chapter-0 release was attributed (some
            // manga DO publish a Chapter 0 prologue). Same genuinely-absent guard as the loop.
            if (wholeNumbers.Contains(0m) && !existingNumbers.Contains(0m))
            {
                rows.Add(BuildRow(0m, null, null, null, monitored, ChapterType.Regular));
            }

            if (rows.Count == 0)
            {
                return 0;
            }

            _logger.Debug(
                "Synthesizing {0} chapter row(s) for manga '{1}' (id={2}) from gateway releases.",
                rows.Count,
                manga.Title,
                manga.Id);

            _chapterListService.SyncChapters(manga, rows, preserveExistingOnNull: true);

            return rows.Count;
        }

        public IReadOnlyList<Chapter> SynthesizeForGrab(RemoteChapter remoteChapter)
        {
            var manga = remoteChapter?.Manga;
            var numbers = remoteChapter?.ParsedChapterInfo?.ChapterNumbers;

            if (manga == null || numbers == null || numbers.Length == 0)
            {
                return Array.Empty<Chapter>();
            }

            // The grabbed number is whole OR fractional (D-04). A grab targets a single
            // chapter; take the parsed number(s) and synthesize only the absent ones. Track
            // the resolved row for every grabbed number (already-existing OR newly-synthesized)
            // so the caller can re-hydrate RemoteChapter.Chapters (WR-01).
            var monitored = ResolveMonitored(manga);
            var resolved = new List<Chapter>();
            var rows = new List<Chapter>();

            foreach (var number in numbers.Distinct())
            {
                var existing = _chapterService.FindByMangaAndNumber(manga.Id, number);
                if (existing != null)
                {
                    resolved.Add(existing);
                    continue;
                }

                var parsed = remoteChapter.ParsedChapterInfo;
                rows.Add(BuildRow(
                    number,
                    parsed?.VolumeNumber,
                    ResolveReleaseDate(remoteChapter),
                    ResolveExternalId(remoteChapter),
                    monitored,
                    parsed?.ChapterType ?? ChapterType.Regular));
            }

            if (rows.Count == 0)
            {
                return resolved;
            }

            _logger.Debug(
                "Synthesizing {0} grabbed chapter row(s) for manga '{1}' (id={2}).",
                rows.Count,
                manga.Title,
                manga.Id);

            _chapterListService.SyncChapters(manga, rows, preserveExistingOnNull: true);

            // Re-read each synthesized number so the returned rows carry their persisted Id
            // (robust regardless of whether InsertMany back-fills the in-memory objects).
            foreach (var row in rows)
            {
                var saved = _chapterService.FindByMangaAndNumber(manga.Id, row.ChapterNumber);
                if (saved != null)
                {
                    resolved.Add(saved);
                }
            }

            return resolved;
        }

        // (1) Precision-first attribution gate. ID-match runs FIRST when Ids present; else
        // EXACT-title (Strategies 1+2 of MangaParsingService.GetManga). The inexact
        // substring/fuzzy Strategy 3 is EXCLUDED — a fuzzy substring match is exactly the
        // "Killing Field" → "The Forgotten Field" cross-title corruption we must prevent.
        private bool BelongsToSearchedManga(MangaDownloadDecision decision, Manga searched)
        {
            var remote = decision?.RemoteChapter;
            if (remote == null)
            {
                return false;
            }

            // ID branch (authoritative). Defensive parse — the gateway is untrusted upstream;
            // never throw on a malformed value.
            var ids = remote.Release?.Ids;
            if (ids != null && IdsMatch(ids, searched))
            {
                return true;
            }

            // EXACT-title branch.
            var rawTitle = remote.ParsedChapterInfo?.MangaTitle;
            if (string.IsNullOrWhiteSpace(rawTitle))
            {
                return false;
            }

            var clean = MangaTitleNormalizer.Normalize(rawTitle);
            if (string.IsNullOrWhiteSpace(clean))
            {
                return false;
            }

            var hit = _mangaService.FindByTitle(clean) ?? _mangaService.FindByAlternativeTitle(clean);
            return hit != null && hit.Id == searched.Id;
        }

        private static bool IdsMatch(IDictionary<string, object> ids, Manga searched)
        {
            // Case-insensitive key lookup; frozen gateway keys are mangadexId / anilistId / malId.
            string Get(string key)
            {
                var pair = ids.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase));
                return pair.Value?.ToString();
            }

            var mangaDexRaw = Get("mangadexId");
            if (searched.MangaDexId.HasValue
                && Guid.TryParse(mangaDexRaw, out var mangaDexId)
                && mangaDexId == searched.MangaDexId.Value)
            {
                return true;
            }

            var aniListRaw = Get("anilistId");
            if (searched.AniListId.HasValue
                && int.TryParse(aniListRaw, out var aniListId)
                && aniListId == searched.AniListId.Value)
            {
                return true;
            }

            var malRaw = Get("malId");
            if (searched.MalId.HasValue
                && int.TryParse(malRaw, out var malId)
                && malId == searched.MalId.Value)
            {
                return true;
            }

            return false;
        }

        // (4) Monitor policy — synthesis OWNS this decision. Do NOT delegate to the add-time
        // monitor service (it reads the add-time MangaMonitor enum, not MonitorNewItems).
        // default(0) == All.
        private static bool ResolveMonitored(Manga manga)
        {
            return manga.MonitorNewItems switch
            {
                MangaMonitorNewItems.All => true,
                MangaMonitorNewItems.None => false,
                _ => true,
            };
        }

        private static DateTime? ResolveReleaseDate(RemoteChapter remoteChapter)
        {
            var publish = remoteChapter.Release?.PublishDate;
            return publish.HasValue && publish.Value != default ? publish : null;
        }

        private static string ResolveExternalId(RemoteChapter remoteChapter)
        {
            var ids = remoteChapter.Release?.Ids;
            if (ids == null)
            {
                return null;
            }

            var pair = ids.FirstOrDefault(kv => string.Equals(kv.Key, "mangadexId", StringComparison.OrdinalIgnoreCase));
            return pair.Value?.ToString();
        }

        // (5) Field map → Chapter. Title is intentionally null (renders "Chapter N");
        // leaving MangaId for SyncChapters to stamp on insert.
        private static Chapter BuildRow(
            decimal number,
            int? volumeNumber,
            DateTime? firstReleaseDate,
            string externalId,
            bool monitored,
            ChapterType chapterType = ChapterType.Regular)
        {
            return new Chapter
            {
                ChapterNumber = number,
                VolumeNumber = volumeNumber,
                FirstReleaseDate = firstReleaseDate,
                ExternalId = externalId,
                Title = null,
                ChapterType = chapterType,
                ChapterFileId = null,
                Monitored = monitored,
            };
        }
    }
}
