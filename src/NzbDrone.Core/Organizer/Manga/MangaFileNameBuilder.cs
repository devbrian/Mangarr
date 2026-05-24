using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Parser.Model;
using MangaModel = NzbDrone.Core.Manga.Manga;

namespace NzbDrone.Core.Organizer.Manga
{
    // Sonarr divergence: NEW manga-side file-name builder per Phase 5 D-14 — see DIVERGENCE.md.
    // Reuses Sonarr's FileNameBuilder.TitleRegex shape (lines 48-49) — DO NOT REINVENT padding semantics.
    // Padding uses C# canonical IFormattable.ToString("000.0", CultureInfo.InvariantCulture) per
    // Pitfall 7 — NOT custom regex padding logic. The customFormat character class is widened to
    // include '.' so `:000.0` decimal-pad specs work (Sonarr's regex does NOT permit '.').
    // Phase 8 cleanup: collapse to canonical FileNameBuilder when Tv/ deletes.
    public class MangaFileNameBuilder : IBuildMangaFileNames
    {
        // Token regex — Sonarr's FileNameBuilder.TitleRegex shape with widened customFormat class
        // to admit '.' (for :000.0 chapter-number decimal padding).
        private static readonly Regex TitleRegex = new Regex(
            @"(?<escaped>\{\{|\}\})|\{(?<prefix>[- ._\[(]*)(?<token>(?:[a-z0-9]+)(?:(?<separator>[- ._]+)(?:[a-z0-9]+))?)(?::(?<customFormat>[ ,a-z0-9+\-.]+(?<![- ])))?(?<suffix>[- ._)\]]*)\}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // NEW manga-side ChapterRegex sibling — supports :000 / :0000 / :000.0 spec per D-14.
        // Sibling to FileNameBuilder.EpisodeRegex (line 51-52). Used for has-token detection
        // (e.g., is the chapter number present in the format string at all).
        public static readonly Regex ChapterRegex = new Regex(
            @"(?<chapter>\{chapter(?:\:0+(?:\.0+)?)?})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex FileNameCleanupRegex = new Regex(@"([- ._])(\1)+", RegexOptions.Compiled);
        private static readonly Regex TrimSeparatorsRegex = new Regex(@"[- ._]+$", RegexOptions.Compiled);

        private readonly INamingConfigService _namingConfigService;

        // Phase 16.1 fix-forward: depend on IChapterFileRepository, not IChapterFileService.
        // IChapterFileService takes IMangaService in its ctor, which closes a DI cycle through
        // MangaService -> MangaPathBuilder -> MangaFileNameBuilder. The repo exposes the same
        // GetFilesByChapter(int) signature with no IMangaService dependency.
        private readonly IChapterFileRepository _chapterFileRepository;
        private readonly Logger _logger;

        public MangaFileNameBuilder(INamingConfigService namingConfigService,
                                    IChapterFileRepository chapterFileRepository,
                                    Logger logger)
        {
            _namingConfigService = namingConfigService;
            _chapterFileRepository = chapterFileRepository;
            _logger = logger;
        }

        public string BuildFileName(
            List<NzbDrone.Core.Manga.Chapter> chapters,
            MangaModel manga,
            ReleaseInfo release = null,
            string extension = "",
            NamingConfig namingConfig = null,
            List<CustomFormat> customFormats = null)
        {
            namingConfig ??= _namingConfigService.GetConfig();

            var pattern = namingConfig.StandardChapterFormat.IsNullOrWhiteSpace()
                ? MangaNamingPresets.Default.StandardChapterFormat
                : namingConfig.StandardChapterFormat;

            var result = ResolveTokens(pattern, manga, chapters, release);
            result = SanitizeFileName(result, namingConfig);

            // Tidy duplicate separators and trailing separators (Sonarr precedent).
            result = FileNameCleanupRegex.Replace(result, match => match.Captures[0].Value[0].ToString());
            result = TrimSeparatorsRegex.Replace(result, string.Empty);

            return result + (extension ?? string.Empty);
        }

        public string BuildFilePath(
            List<NzbDrone.Core.Manga.Chapter> chapters,
            MangaModel manga,
            ReleaseInfo release,
            string extension,
            NamingConfig namingConfig = null,
            List<CustomFormat> customFormats = null)
        {
            // D-15 flat folder layout: <root>/<MangaFolderFormat>/<chapterFileName>
            namingConfig ??= _namingConfigService.GetConfig();

            var fileName = BuildFileName(chapters, manga, release, extension, namingConfig, customFormats);

            // Prefer manga.Path if set (existing on-disk folder); fall back to root + computed manga folder.
            if (manga.Path.IsNotNullOrWhiteSpace())
            {
                return Path.Combine(manga.Path, fileName);
            }

            var mangaFolder = GetMangaFolder(manga, namingConfig);
            return Path.Combine(manga.RootFolderPath ?? string.Empty, mangaFolder, fileName);
        }

        public string GetMangaFolder(MangaModel manga, NamingConfig namingConfig = null)
        {
            namingConfig ??= _namingConfigService.GetConfig();

            var pattern = namingConfig.MangaFolderFormat.IsNullOrWhiteSpace()
                ? MangaNamingPresets.Default.MangaFolderFormat
                : namingConfig.MangaFolderFormat;

            var result = ResolveTokens(pattern, manga, chapters: null, release: null);
            var sanitized = SanitizeFileName(result, namingConfig);

            // WR-07: empty-folder collision guard. If MangaFolderFormat resolved to an empty
            // string (e.g. Manga.Title is "" / whitespace, or every token in the pattern was
            // null), Path.Combine(root, "") collapses to root and the chapter file ends up
            // in the root folder, mixing with other manga and tripping up the reader-app
            // one-level-deep scan convention. Multiple manga with empty resolved titles all
            // collide on identical chapter filenames. Fall back to "Manga {Id}" so each
            // manga gets a unique folder even when title resolution fails.
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                var fallbackId = manga?.Id > 0 ? manga.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) : "unknown";
                sanitized = $"Manga {fallbackId}";
                _logger.Warn(
                    "MangaFolderFormat resolved to empty for manga Id={0}, Title='{1}'; using fallback folder name '{2}' to avoid root-folder collisions",
                    manga?.Id,
                    manga?.Title,
                    sanitized);
            }

            return sanitized;
        }

        // ── Token resolution ────────────────────────────────────────────────────────────────

        private string ResolveTokens(string pattern, MangaModel manga, List<NzbDrone.Core.Manga.Chapter> chapters, ReleaseInfo release)
        {
            return TitleRegex.Replace(pattern, match =>
            {
                if (match.Groups["escaped"].Success)
                {
                    return match.Value.Substring(1, 1);
                }

                var token = match.Groups["token"].Value.ToLowerInvariant();

                // Strip any internal whitespace/separator from the token so `{Manga Title}`
                // and `{Manga.Title}` both resolve to the same handler.
                var canonicalToken = NormalizeToken(token);

                var formatSpec = match.Groups["customFormat"].Value;
                var prefix = match.Groups["prefix"].Value;
                var suffix = match.Groups["suffix"].Value;

                var resolved = ResolveTokenValue(canonicalToken, formatSpec, manga, chapters, release);

                if (resolved.IsNullOrWhiteSpace())
                {
                    return string.Empty;
                }

                return prefix + resolved + suffix;
            });
        }

        private static string NormalizeToken(string token)
        {
            // Tokens may use either '.' or whitespace as separator; canonicalize to '.'
            // so `manga title`, `manga.title`, and `manga_title` all map to "manga.title".
            // Fold any [-_. ] sequences into a single '.'.
            var trimmed = token.Trim();
            var canonical = Regex.Replace(trimmed, @"[\-_.\s]+", ".");
            return canonical.ToLowerInvariant();
        }

        private string ResolveTokenValue(
            string canonicalToken,
            string formatSpec,
            MangaModel manga,
            List<NzbDrone.Core.Manga.Chapter> chapters,
            ReleaseInfo release)
        {
            // D-14 token set — see Phase 5 CONTEXT item 11 + RESEARCH §"Token semantics".
            // Note: Phase 2 D-09 — Manga.MangaDexId is Guid? (singular), Manga.MalId is int? (singular),
            // Manga.AniListId is int? (singular). NOT collections (the plan's pseudocode used
            // MalIds[].FirstOrDefault but the actual entity is singular per Phase 2 schema).
            switch (canonicalToken)
            {
                case "manga.title":
                    return manga?.Title;

                case "manga.mangadexid":
                    return manga?.MangaDexId?.ToString();

                case "manga.malid":
                    return manga?.MalId?.ToString(CultureInfo.InvariantCulture);

                case "manga.anilistid":
                    return manga?.AniListId?.ToString(CultureInfo.InvariantCulture);

                case "chapter.number":
                    return FormatChapterNumber(chapters?.FirstOrDefault()?.ChapterNumber, formatSpec);

                case "chapter.title":
                    return chapters?.FirstOrDefault()?.Title;

                case "scanlationgroup":
                case "scanlation.group":
                    // Phase 16.1: ScanlationGroup is canonical on ChapterFile (D-06). Indexer's
                    // ReleaseInfo wins (Phase 3 indexer pipeline carries the selected-release
                    // context); on a rename / disk-import path with no release, fall back to
                    // the matched Chapter's existing ChapterFile.ScanlationGroup.
                    return release?.ScanlationGroup
                        ?? GetChapterFileScanlationGroup(chapters);

                case "language":
                    // Phase 16.1: TranslatedLanguage is canonical on ChapterFile — same shape as
                    // scanlation group above; release-info wins, fall back to the matched
                    // Chapter's existing ChapterFile.TranslatedLanguage for rename/disk-import.
                    return release?.TranslatedLanguage
                        ?? GetChapterFileLanguage(chapters);

                case "source":
                    // Indexer attribution — D-17 SourceKey. ReleaseInfo.Indexer carries the
                    // user-named instance (e.g. "My MangaDex Mirror"). Per BL-02 the canonical
                    // key (e.g. "mangadex" / "comix.to") is resolved via IIndexerFactory at
                    // decision time on the CF input; the file-naming token here uses the user-
                    // visible instance name on purpose so a renamed indexer surfaces the rename
                    // in filenames. Substrate work to stamp the canonical key on ReleaseInfo
                    // belongs in Phase 6 if we want the {Source} token to use the canonical key.
                    return release?.Indexer;

                // Phase 30 Plan 30-05 (II2-03) — MediaInfo-backed tokens. Render empty
                // (null) when the relevant subfield is null per D-09 (no "0 pages" /
                // "Unknown" defaults); ResolveTokens line 159-162 converts null -> empty.
                case "page.count":
                case "pagecount":
                    return GetChapterFileMediaInfo(chapters)?.PageCount?.ToString(CultureInfo.InvariantCulture);

                case "color":
                    var color = GetChapterFileMediaInfo(chapters)?.Color;
                    return color switch { true => "Color", false => "B&W", null => null };

                case "dpi":
                    return GetChapterFileMediaInfo(chapters)?.DpiHorizontal?.ToString(CultureInfo.InvariantCulture);

                default:
                    return null;   // unknown token — silently dropped (Sonarr precedent)
            }
        }

        // Pitfall 7 mitigation — use C# canonical IFormattable.ToString with InvariantCulture.
        // DO NOT write custom padding regex. Pattern S10 [SetCulture("de-DE")] regression cell
        // in MangaFileNameBuilderFixture asserts "042.5" not "0042,5".
        private static string FormatChapterNumber(decimal? chapterNumber, string formatSpec)
        {
            if (!chapterNumber.HasValue)
            {
                return string.Empty;
            }

            if (string.IsNullOrEmpty(formatSpec))
            {
                // Default: integer if whole, else preserve decimal — InvariantCulture so "42.5" not "42,5"
                return chapterNumber.Value.ToString(CultureInfo.InvariantCulture);
            }

            // C# canonical IFormattable — culture-invariant (Pitfall 7).
            return chapterNumber.Value.ToString(formatSpec, CultureInfo.InvariantCulture);
        }

        // Sanitize for filename use — mirrors FileNameBuilder.CleanFileName semantics:
        // colon replacement + bad-character stripping. Does NOT touch the token resolver.
        private static string SanitizeFileName(string input, NamingConfig namingConfig)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            var result = input;

            // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — ColonReplacementFormat enum
            // and FileNameBuilder.Bad/GoodCharacters constants stripped per Plan 15-10 Organizer/
            // FileNameBuilder.cs DELETE. ColonReplacementFormat retyped int (schema round-trip);
            // values: 0=Smart, 1=Dash, 2=SpaceDash, 3=SpaceDashSpace, 4=Custom (Sonarr ordinal).
            const int colonSmart = 0;
            const int colonDash = 1;
            const int colonSpaceDash = 2;
            const int colonSpaceDashSpace = 3;
            const int colonCustom = 4;

            if (namingConfig.ReplaceIllegalCharacters)
            {
                if (namingConfig.ColonReplacementFormat == colonSmart)
                {
                    result = result.Replace(": ", " - ");
                    result = result.Replace(":", "-");
                }
                else
                {
                    var replacement = namingConfig.ColonReplacementFormat switch
                    {
                        colonDash => "-",
                        colonSpaceDash => " -",
                        colonSpaceDashSpace => " - ",
                        colonCustom => namingConfig.CustomColonReplacementFormat ?? string.Empty,
                        _ => string.Empty
                    };
                    result = result.Replace(":", replacement);
                }
            }
            else
            {
                result = result.Replace(":", string.Empty);
            }

            // Bad/Good character replacement — Sonarr's TV FileNameBuilder defined these inline; restore as locals.
            var badChars = new[] { "\\", "/", "<", ">", "?", "*", "|", "\"" };
            var goodChars = new[] { "+", "+", "{", "}", "!", "-", "", "" };
            for (var i = 0; i < badChars.Length; i++)
            {
                result = result.Replace(
                    badChars[i],
                    namingConfig.ReplaceIllegalCharacters ? goodChars[i] : string.Empty);
            }

            return result.TrimStart(' ', '.').TrimEnd(' ');
        }

        // Phase 16.1 fallback helpers — when the {ScanlationGroup} / {Language} tokens are
        // evaluated without a populated release context (e.g., rename/disk-import path),
        // resolve from the matched Chapter's existing ChapterFile (which carries the canonical
        // TranslatedLanguage + ScanlationGroup post-import per Phase 6 PIPELINE-04 + D-06).
        private string GetChapterFileScanlationGroup(List<NzbDrone.Core.Manga.Chapter> chapters)
        {
            var chapterId = chapters?.FirstOrDefault()?.Id ?? 0;
            if (chapterId == 0)
            {
                return null;
            }

            return _chapterFileRepository.GetFilesByChapter(chapterId).FirstOrDefault()?.ScanlationGroup;
        }

        private string GetChapterFileLanguage(List<NzbDrone.Core.Manga.Chapter> chapters)
        {
            var chapterId = chapters?.FirstOrDefault()?.Id ?? 0;
            if (chapterId == 0)
            {
                return null;
            }

            return _chapterFileRepository.GetFilesByChapter(chapterId).FirstOrDefault()?.TranslatedLanguage;
        }

        // Phase 30 Plan 30-05 (II2-03) — sibling to GetChapterFileScanlationGroup /
        // GetChapterFileLanguage above. Resolves the ChapterFile.MediaInfo blob for
        // the first chapter in the list so {Page Count} / {Color} / {DPI} tokens can
        // read PageCount / Color / DpiHorizontal. Returns null when the chapter has
        // no file or the MediaInfo column is NULL (pre-Migration-004 rows per D-05);
        // the token resolver renders null as empty per D-09 null-skip.
        private ChapterMediaInfo GetChapterFileMediaInfo(List<NzbDrone.Core.Manga.Chapter> chapters)
        {
            var chapterId = chapters?.FirstOrDefault()?.Id ?? 0;
            if (chapterId == 0)
            {
                return null;
            }

            // Defensive null-guard on the repo return: when no ChapterFile rows exist
            // for the chapter (or, in tests, when the repo mock has no setup for this
            // id), GetFilesByChapter may return null; .FirstOrDefault() on null throws.
            var files = _chapterFileRepository.GetFilesByChapter(chapterId);
            return files?.FirstOrDefault()?.MediaInfo;
        }
    }
}
