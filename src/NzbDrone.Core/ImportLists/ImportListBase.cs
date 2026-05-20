using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation.Results;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.ImportLists
{
    // Phase 26 Plan 26-04 — ported from
    // .planning/reference/sonarr-vertical-slices/import-lists/ImportListBase.cs with
    // manga-shape swaps:
    //   * ImportListFetchResult.Series → .Manga (RESEARCH §Q1; result list semantics
    //                                            unchanged, property rename only).
    //   * CleanupListItems dedup key   : (Title, TvdbId, ImdbId)
    //                                  → (Title, MangaDexId, MalId, AniListId) per the
    //                                    manga-ID triplet (Pitfall 6 swap source).
    //   * IParsingService              → IMangaParsingService (Mangarr peer; Sonarr
    //                                    parser was DELETED in Phase 15 Plan 15-10).
    //
    // ImportListBase is consumed by HttpImportListBase (HTTP-driven providers) and by
    // future custom providers (program-output / file-system providers) that don't need
    // an HTTP client.

    public class ImportListFetchResult
    {
        public ImportListFetchResult()
        {
            Manga = new List<ImportListItemInfo>();
        }

        public ImportListFetchResult(IEnumerable<ImportListItemInfo> manga, bool anyFailure)
        {
            Manga = manga.ToList();
            AnyFailure = anyFailure;
        }

        public List<ImportListItemInfo> Manga { get; set; }
        public bool AnyFailure { get; set; }
    }

    public abstract class ImportListBase<TSettings> : IMangaImportList
        where TSettings : IImportListSettings, new()
    {
        protected readonly IImportListStatusService _importListStatusService;
        protected readonly IConfigService _configService;
        protected readonly IMangaParsingService _parsingService;
        protected readonly ILocalizationService _localizationService;
        protected readonly Logger _logger;

        public abstract string Name { get; }

        public abstract ImportListType ListType { get; }

        public abstract TimeSpan MinRefreshInterval { get; }

        public ImportListBase(IImportListStatusService importListStatusService, IConfigService configService, IMangaParsingService parsingService, ILocalizationService localizationService, Logger logger)
        {
            _importListStatusService = importListStatusService;
            _configService = configService;
            _parsingService = parsingService;
            _localizationService = localizationService;
            _logger = logger;
        }

        public Type ConfigContract => typeof(TSettings);

        public virtual ProviderMessage Message => null;

        public virtual IEnumerable<ProviderDefinition> DefaultDefinitions
        {
            get
            {
                var config = (IProviderConfig)new TSettings();

                yield return new ImportListDefinition
                {
                    Name = GetType().Name,
                    EnableAutomaticAdd = config.Validate().IsValid,
                    Implementation = GetType().Name,
                    Settings = config
                };
            }
        }

        public virtual ProviderDefinition Definition { get; set; }

        public virtual object RequestAction(string action, IDictionary<string, string> query)
        {
            return null;
        }

        protected TSettings Settings => (TSettings)Definition.Settings;

        public abstract ImportListFetchResult Fetch();

        protected virtual IList<ImportListItemInfo> CleanupListItems(IEnumerable<ImportListItemInfo> releases)
        {
            // Sonarr divergence (Phase 26 Plan 26-04): dedup key swapped from
            // (Title, TvdbId, ImdbId) to manga-ID triplet (Title, MangaDexId, MalId,
            // AniListId) — preserves the substring-Title/cross-source-ID collapse the
            // Sonarr reference relies on for AniList watchlist deduplication.
            var result = releases.DistinctBy(r => new { r.Title, r.MangaDexId, r.MalId, r.AniListId }).ToList();

            result.ForEach(c =>
            {
                c.ImportListId = Definition.Id;
                c.ImportList = Definition.Name;
            });

            return result;
        }

        public ValidationResult Test()
        {
            var failures = new List<ValidationFailure>();

            try
            {
                Test(failures);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Test aborted due to exception");
                failures.Add(new ValidationFailure(string.Empty, _localizationService.GetLocalizedString("ImportListsValidationTestFailed", new Dictionary<string, object> { { "exceptionMessage", ex.Message } })));
            }

            return new ValidationResult(failures);
        }

        protected abstract void Test(List<ValidationFailure> failures);

        public override string ToString()
        {
            return Definition.Name;
        }
    }
}
