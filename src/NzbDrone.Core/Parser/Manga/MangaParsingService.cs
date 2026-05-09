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
        // BCP-47 sentinel used when the parsed release carries no language
        // marker AND the indexer did not supply one. "und" = undefined per
        // RFC 5646 / ISO 639-2 — surfaced into Chapter.TranslatedLanguage where
        // synthetic chapter rows already use the same sentinel (Phase 1 baseline
        // and Plan 02-03 ChapterRepository).
        private const string UndefinedLanguage = "und";

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

            var hit = _mangaService.FindByTitle(clean);
            if (hit == null)
            {
                _logger.Debug("MangaParsingService.GetManga: no match for normalized title '{0}' (raw='{1}')", clean, title);
            }

            return hit;
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
                // TODO(plan-16-03): re-introduce per-translation disambiguation via
                // IChapterReleaseService.GetReleasesByChapter once Wave 2 lands the navigation.
                // Plan 16-02 stub: canonical Chapter is now language-free at the
                // (MangaId, ChapterNumber) grain (Phase 16 STRUCT-01), so Map collapses to
                // lookup-by-(MangaId, ChapterNumber). Language matching moves to the
                // ChapterRelease layer in Plan 16-03.
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

        // TODO(plan-16-03): re-introduce chapter-number-only resolver helpers once Wave 2
        // wires per-translation disambiguation against IChapterReleaseService.
        // Phase 16 STRUCT-01 collapsed the language axis from canonical Chapter — the
        // multi-candidate disambiguation is moot until Plan 16-03 lands the per-language
        // ChapterRelease layer. ResolveByNumberOnly / ResolvePreferredLanguage /
        // ResolveTranslationProfile / GetLanguageRank all removed; consumers moved to the
        // simpler (MangaId, ChapterNumber) lookup path.
    }
}
