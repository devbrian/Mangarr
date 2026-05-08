using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.MetadataSource
{
    // Manga-side parallel of TV's SearchSeriesComparer (Phase 8 audit gap "single",
    // SearchSeriesComparer.md, no_sibling). Ranks IProvideMangaInfo / ISearchForNewManga
    // candidates so the AddManga UI shows best-match-first instead of the upstream
    // metadata source's native ordering. Mirrors SearchSeriesComparer's phase shape
    // verbatim (exact -> article-stripped -> close-match -> year-tiebreak -> prefix
    // match -> Levenshtein-with-year-factor) — diverging only where the manga domain
    // forces it: Manga.PublicationYear (nullable) replaces Series.Year. Per
    // CLAUDE.md "Preserve Mangarr's shape wherever it works; diverge only where the
    // manga domain forces us."
    public class SearchMangaComparer : IComparer<Manga.Manga>
    {
        private static readonly Regex RegexCleanPunctuation = new Regex("[-._:]", RegexOptions.Compiled);
        private static readonly Regex RegexCleanCountryYearPostfix = new Regex(@"(?<=.+)( \([A-Z]{2}\)| \(\d{4}\)| \([A-Z]{2}\) \(\d{4}\))$", RegexOptions.Compiled);
        private static readonly Regex ArticleRegex = new Regex(@"^(a|an|the)\s", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public string SearchQuery { get; private set; }

        private readonly string _searchQueryWithoutYear;
        private int? _year;

        public SearchMangaComparer(string searchQuery)
        {
            SearchQuery = searchQuery;

            var match = Regex.Match(SearchQuery, @"^(?<query>.+)\s+(?:\((?<year>\d{4})\)|(?<year>\d{4}))$");
            if (match.Success)
            {
                _searchQueryWithoutYear = match.Groups["query"].Value.ToLowerInvariant();
                _year = int.Parse(match.Groups["year"].Value);
            }
            else
            {
                _searchQueryWithoutYear = searchQuery.ToLowerInvariant();
            }
        }

        public int Compare(Manga.Manga x, Manga.Manga y)
        {
            var result = 0;

            // Prefer exact matches
            result = Compare(x, y, m => CleanPunctuation(m.Title).Equals(CleanPunctuation(SearchQuery)));
            if (result != 0)
            {
                return -result;
            }

            // Remove Articles (a/an/the)
            result = Compare(x, y, m => CleanArticles(m.Title).Equals(CleanArticles(SearchQuery)));
            if (result != 0)
            {
                return -result;
            }

            // Prefer close matches
            result = Compare(x, y, m => CleanPunctuation(m.Title).LevenshteinDistance(CleanPunctuation(SearchQuery)) <= 1);
            if (result != 0)
            {
                return -result;
            }

            // Compare clean matches by publication year
            result = CompareWithYear(x, y, m => CleanTitle(m.Title).LevenshteinDistance(_searchQueryWithoutYear) <= 1);
            if (result != 0)
            {
                return -result;
            }

            // Compare prefix matches by publication year (e.g., "Vagabond: ...")
            result = CompareWithYear(x, y, m => m.Title.ToLowerInvariant().StartsWith(_searchQueryWithoutYear + ":"));
            if (result != 0)
            {
                return -result;
            }

            return Compare(x, y, m => SearchQuery.LevenshteinDistanceClean(m.Title) - GetYearFactor(m));
        }

        public int Compare<T>(Manga.Manga x, Manga.Manga y, Func<Manga.Manga, T> keySelector)
            where T : IComparable<T>
        {
            var keyX = keySelector(x);
            var keyY = keySelector(y);

            return keyX.CompareTo(keyY);
        }

        public int CompareWithYear(Manga.Manga x, Manga.Manga y, Predicate<Manga.Manga> canMatch)
        {
            var matchX = canMatch(x);
            var matchY = canMatch(y);

            if (matchX && matchY)
            {
                if (_year.HasValue)
                {
                    var result = Compare(x, y, m => m.PublicationYear.GetValueOrDefault() == _year.Value);
                    if (result != 0)
                    {
                        return result;
                    }
                }

                return Compare(x, y, m => m.PublicationYear.GetValueOrDefault());
            }

            return matchX.CompareTo(matchY);
        }

        private string CleanPunctuation(string title)
        {
            title = RegexCleanPunctuation.Replace(title, "");

            return title.ToLowerInvariant();
        }

        private string CleanTitle(string title)
        {
            title = RegexCleanPunctuation.Replace(title, "");
            title = RegexCleanCountryYearPostfix.Replace(title, "");

            return title.ToLowerInvariant();
        }

        private string CleanArticles(string title)
        {
            title = ArticleRegex.Replace(title, "");

            return title.Trim().ToLowerInvariant();
        }

        private int GetYearFactor(Manga.Manga manga)
        {
            if (_year.HasValue && manga.PublicationYear.HasValue)
            {
                var offset = Math.Abs(manga.PublicationYear.Value - _year.Value);
                if (offset <= 1)
                {
                    return 20 - (10 * offset);
                }
            }

            return 0;
        }
    }
}
