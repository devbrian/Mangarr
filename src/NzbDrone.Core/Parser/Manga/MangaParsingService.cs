using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Profiles.Translations;

namespace NzbDrone.Core.Parser.Manga
{
    // DB-mapping layer for the Parser/Manga tree. Mirrors Mangarr's IParsingService /
    // ParsingService precedent (Parser/ParsingService.cs:14) per 02-CONTEXT.md D-03;
    // the TV peer stays untouched until Phase 8.
    //
    // The parsing pipeline runs in two passes:
    //   1. MangaParser.ParseChapterTitle (pure function, no DI) extracts the parsed
    //      shape from a raw release title.
    //   2. MangaParsingService.Map (this service) resolves the parsed shape against
    //      the local DB — finds the matching Manga aggregate, then fans out the
    //      ChapterNumbers into existing Chapter rows via IChapterService.
    //
    // D-10 contract — language precedence:
    //
    //   The TranslatedLanguage carried on the input ParsedChapterInfo is the
    //   PARSER-EXTRACTED FALLBACK. Phase 3 indexer plugins MUST overwrite
    //   parsedInfo.TranslatedLanguage with the API-supplied (indexer-supplied)
    //   value BEFORE invoking Map() when the indexer has it. This service does
    //   NOT re-derive language; it consumes whatever parsedInfo carries.
    //
    //   In other words, indexer-supplied wins; parser fallback applies only when
    //   the indexer omits language metadata.
    //
    // Issue #30 fix — language-fallback match:
    //
    //   When NEITHER indexer NOR parser supplies a language (e.g. a CBZ file dropped
    //   into the manga's library folder named `<Title> - Chapter NNN.cbz` with no
    //   language tag), the previous strict equality match against the "und" sentinel
    //   silently failed because real DB chapters synced from MangaDex carry their
    //   actual API-supplied language code (e.g. "en"). That left LocalChapter.Chapter
    //   null and the disk-scan / manual-import import-decision pipeline silently
    //   skipped the file at the post-decision `if (lc.Chapter == null)` guard.
    //
    //   Fix: when parsedInfo.TranslatedLanguage is null/empty (i.e. NO language signal
    //   from any source), broaden the chapter lookup to (mangaId, chapterNumber) and
    //   prefer chapters whose language ranks earliest in the manga's TranslationProfile
    //   preference order. The manga's TranslationProfile (or, if null, the global
    //   DefaultTranslationProfile) provides the disambiguator. When a language IS
    //   supplied, the strict equality match is preserved verbatim.
    public interface IMangaParsingService
    {
        // Find the Manga aggregate that matches a free-form release title. Used
        // by Phase 3 search/grab paths that have a string release title and need
        // to identify which Manga the release belongs to.
        NzbDrone.Core.Manga.Manga GetManga(string title);

        // Resolve a parsed release into a RemoteChapter against the local DB.
        // Returns null when manga is null (D-03 contract — caller is expected
        // to short-circuit and skip the release).
        //
        // existingChapters lets the caller pre-load the Manga's chapter set
        // (typical for batch search/grab pipelines that already hold the list).
        // Falls back to IChapterService.FindByMangaAndNumber when a parsed
        // ChapterNumber is not present in the pre-loaded set.
        RemoteChapter Map(ParsedChapterInfo parsedChapterInfo, NzbDrone.Core.Manga.Manga manga, IList<Chapter> existingChapters);
    }

    public class MangaParsingService : IMangaParsingService
    {
        // Note: per-translation language data lives at indexer-projection grain
        // (RemoteChapter.Release.TranslatedLanguage) and at file-import grain
        // (ChapterFile.TranslatedLanguage — Phase 6 PIPELINE-04). The DecisionEngine
        // specs target the indexer projection; ChapterFile carries the post-import
        // per-translation axis. The canonical Chapter row has no language axis.

        private readonly IMangaService _mangaService;
        private readonly IChapterService _chapterService;
        private readonly ITranslationProfileService _translationProfileService;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public MangaParsingService(
            IMangaService mangaService,
            IChapterService chapterService,
            ITranslationProfileService translationProfileService,
            IConfigService configService,
            Logger logger)
        {
            _mangaService = mangaService;
            _chapterService = chapterService;
            _translationProfileService = translationProfileService;
            _configService = configService;
            _logger = logger;
        }

        public NzbDrone.Core.Manga.Manga GetManga(string title)
        {
            // GH #118 — Sonarr-canonical multi-strategy resolution mirroring
            // ParsingService.GetSeries (deleted in Phase 15; preserved logic at
            // commit 2832eecb^). Sonarr layers SceneMapping → FindByTitle →
            // AllTitles variants → year-aware → FindByTitleInexact. Mangarr's
            // analog is FindByTitle → AlternativeTitles → FindByTitleInexact,
            // with AlternativeTitles standing in for Sonarr's SceneMapping
            // alias dataset (populated by metadata sources from each provider's
            // alt-title field set).
            //
            // The 77a114221 fix for DEF-19-02-01 short-circuited THIS method on
            // the search path with `searchCriteria.Manga ?? GetManga(...)` in
            // MangaDownloadDecisionMaker, precisely because Strategy 1 alone
            // could not resolve MangaDex's romanized attributes.title against
            // the English-stored Manga.CleanTitle. Adding Strategy 2 (alt-title
            // match) restores GetManga as the correct resolver and lets the
            // existing MangaSpecification do its designed Id-cross-check job
            // (catching cross-title indexer noise where a release belongs to a
            // different manga than the search target).
            var parsed = MangaParser.ParseChapterTitle(title);

            // If parsing failed entirely, search the raw title — preserves
            // ability to look up titles that the parser doesn't recognize as a
            // chapter release (e.g., user-pasted strings). WR-14 fix: log a
            // debug-level breadcrumb so Phase 3 indexer troubleshooting can
            // see when a title fell through the parser short-path; the raw
            // title is usually filename junk that won't match any
            // Manga.CleanTitle, but the debug entry surfaces the case rather
            // than silently returning null.
            if (parsed == null)
            {
                _logger.Debug("MangaParsingService.GetManga: parser returned null for {0}; falling back to raw title search", title);
            }

            var searchTitle = parsed?.MangaTitle ?? title;
            if (string.IsNullOrWhiteSpace(searchTitle))
            {
                return null;
            }

            // Single source of truth normalization (D-05) so this service stays
            // consistent with AddManga dedup + CrossSourceIdResolver lookups.
            var clean = MangaTitleNormalizer.Normalize(searchTitle);
            if (string.IsNullOrWhiteSpace(clean))
            {
                return null;
            }

            // Strategy 1: direct CleanTitle match (existing path).
            var hit = _mangaService.FindByTitle(clean);
            if (hit != null)
            {
                return hit;
            }

            // Strategy 2: alt-title match. Mangarr's analog of Sonarr's
            // _sceneMappingService.FindTvdbId step in ParsingService.GetSeries.
            // The metadata source persists each provider's alt-title set
            // pre-normalized at write-time, so this lookup is a direct
            // canonical-vs-canonical comparison. quick-260618-eqz — the broadened
            // FindByAlternativeTitle now resolves via the metadata AlternativeTitles
            // column OR the user-owned UserAlternativeTitles column, so a release
            // whose name only the user supplied (no metadata source knows it) still
            // resolves here. No call-site change — the dual-list coverage comes for
            // free from the broadened repository query.
            hit = _mangaService.FindByAlternativeTitle(clean);
            if (hit != null)
            {
                _logger.Debug(
                    "MangaParsingService.GetManga: resolved '{0}' via AlternativeTitles match on Manga '{1}' (id={2})",
                    clean,
                    hit.Title,
                    hit.Id);
                return hit;
            }

            // Strategy 3: substring fallback — GUARDED against cross-title corruption.
            //
            // debug `flow-wrong-manga-not-rejected` (2026-06-19): the prior unguarded
            // form returned the single FindByTitleInexact candidate on ANY infix
            // containment. FindByTitleInexact is `instr(@releaseClean, Manga.CleanTitle)`
            // — it matches when a library manga's CleanTitle appears ANYWHERE inside the
            // release's clean title. A short library CleanTitle is an infix of countless
            // unrelated longer titles: `flow` ⊂ `thatwhichflowsby` (...which**flows**by),
            // `anz` ⊂ `girlsundp**anz**er`/`d**anz**aisareta`, etc. With exactly one
            // library manga matching, GetManga returned that wrong manga as a "confident"
            // single hit, so MangaSpecification's `subject.Manga.Id == searchCriteria.Manga.Id`
            // was trivially true and the wrong-manga gateway release was ACCEPTED, grabbed,
            // and imported (live: 268 "That Which Flows By" chapters imported under "Flow").
            //
            // This is the SAME class of bug ChapterSynthesisService.BelongsToSearchedManga
            // already guards against — its comment: "a fuzzy substring match is exactly the
            // cross-title corruption we must prevent" (D-06/D-07). We bring this resolver up
            // to that precision posture: a FindByTitleInexact candidate is accepted ONLY when
            // its CleanTitle is EXACTLY EQUAL to the normalized release title (Strategy 3
            // confirms, it never broadens). The earlier prefix-leniency variant was dropped
            // because a length ratio cannot distinguish edition noise (`My Title HD`) from a
            // distinct sequel/variant work (`My Succubus Girlfriend NEW`, `Solo Leveling
            // Ragnarok`) — see IsSafeInexactMatch. Genuine same-title releases resolve via
            // Strategy 1 / Strategy 2; ambiguous (>1) returns null (Sonarr UnknownManga
            // posture). Durable cure: stable source-ID targeting (Phase 37 IN-01, roadmapped).
            var inexactCandidates = _mangaService.FindByTitleInexact(searchTitle);
            if (inexactCandidates != null && inexactCandidates.Count == 1)
            {
                var candidate = inexactCandidates[0];
                var candidateClean = candidate.CleanTitle ?? MangaTitleNormalizer.Normalize(candidate.Title);

                if (IsSafeInexactMatch(clean, candidateClean))
                {
                    _logger.Debug(
                        "MangaParsingService.GetManga: resolved '{0}' via guarded FindByTitleInexact exact-title confirm on Manga '{1}' (id={2})",
                        clean,
                        candidate.Title,
                        candidate.Id);
                    return candidate;
                }

                _logger.Debug(
                    "MangaParsingService.GetManga: REJECTED inexact candidate '{0}' (id={1}, clean='{2}') for release '{3}' — substring/prefix only, not an exact-title match (cross-title corruption guard).",
                    candidate.Title,
                    candidate.Id,
                    candidateClean,
                    clean);
            }

            _logger.Debug(
                "MangaParsingService.GetManga: no match for normalized title '{0}' (raw='{1}'; inexact candidates={2})",
                clean,
                title,
                inexactCandidates?.Count ?? 0);

            return null;
        }

        // Guard for Strategy 3 (debug `flow-wrong-manga-not-rejected`). An inexact
        // FindByTitleInexact candidate is only a SAFE resolution when its CleanTitle is
        // EXACTLY EQUAL to the normalized release title — i.e. Strategy 3 confirms, it
        // never broadens.
        //
        //   releaseClean   = the normalized release/manga title we are resolving
        //   candidateClean = the library manga's CleanTitle that FindByTitleInexact matched
        //
        // History / why exact-only (debug `flow-wrong-manga-not-rejected`, follow-up
        // 2026-06-19): the first guard accepted a PREFIX match when the candidate was
        // >=80% of the release length, to keep "long distinctive title + short trailing
        // edition token" leniency. But a length ratio cannot tell EDITION NOISE
        // (`My Title HD`, same manga) from a DISTINCT SEQUEL/VARIANT work
        // (`My Succubus Girlfriend NEW`, `Solo Leveling Ragnarok`, `My Title Season 2`) —
        // those distinguishers are short, so they sail over any length floor and the wrong
        // (base) manga is resolved. In the manga domain that mis-resolution is the common
        // case, and the parser already strips BRACKETED edition/format noise before this
        // method runs, so a bare trailing word that survives is far more likely a distinct
        // work than edition noise. We therefore drop prefix leniency entirely and require
        // exact equality — the same precision posture ChapterSynthesisService.BelongsToSearchedManga
        // enforces ("a fuzzy substring match is exactly the cross-title corruption we must
        // prevent", D-06/D-07). Genuine same-title releases still resolve via Strategy 1
        // (exact CleanTitle) / Strategy 2 (alt-title). The durable cure for query-text-only
        // gateway attribution is stable source-ID targeting (Phase 37 IN-01, roadmapped).
        private static bool IsSafeInexactMatch(string releaseClean, string candidateClean)
        {
            if (string.IsNullOrEmpty(releaseClean) || string.IsNullOrEmpty(candidateClean))
            {
                return false;
            }

            // Exact-only: a substring/prefix relationship is never trusted on its own —
            // it cannot distinguish edition noise from a distinct sequel/variant title.
            return string.Equals(releaseClean, candidateClean, System.StringComparison.Ordinal);
        }

        public RemoteChapter Map(ParsedChapterInfo parsedChapterInfo, NzbDrone.Core.Manga.Manga manga, IList<Chapter> existingChapters)
        {
            if (manga == null)
            {
                _logger.Trace(
                    "MangaParsingService.Map: null manga for parsed release {0}",
                    parsedChapterInfo?.ReleaseTitle);
                return null;
            }

            var chapters = new List<Chapter>();

            if (parsedChapterInfo?.ChapterNumbers != null)
            {
                // Phase 16 STRUCT-01 + Phase 16.1: canonical Chapter is language-free at
                // the (MangaId, ChapterNumber) grain. Map resolves to the canonical row;
                // per-translation matching (parsedChapterInfo.TranslatedLanguage against
                // RemoteChapter.Release.TranslatedLanguage at indexer-projection grain
                // and against ChapterFile.TranslatedLanguage at file-import grain) lives
                // in the DecisionEngine specifications (Phase 16.1 — Sonarr-canonical
                // pattern; specs target RemoteChapter.Release.TranslatedLanguage, NOT
                // a persistent per-translation entity).
                foreach (var num in parsedChapterInfo.ChapterNumbers)
                {
                    var ch = existingChapters?.FirstOrDefault(c =>
                        c.MangaId == manga.Id
                        && c.ChapterNumber == num);

                    ch ??= _chapterService.FindByMangaAndNumber(manga.Id, num);

                    if (ch != null)
                    {
                        chapters.Add(ch);
                    }
                }
            }

            return new RemoteChapter
            {
                ParsedChapterInfo = parsedChapterInfo,
                Manga = manga,
                Chapters = chapters,
            };
        }

        // Phase 16 STRUCT-01 + STRUCT-04 cleanup: the pre-Phase-16
        // ResolveByNumberOnly / ResolvePreferredLanguage / ResolveTranslationProfile
        // / GetLanguageRank helpers are gone (consumers moved to the simpler
        // (MangaId, ChapterNumber) lookup above). The multi-language disambiguation
        // lives in the DecisionEngine specs (Phase 16.1 Sonarr-canonical pattern) —
        // the indexer's ReleaseInfo carries the language code; the spec scores the
        // candidate release against the manga's TranslationProfile.
    }
}
