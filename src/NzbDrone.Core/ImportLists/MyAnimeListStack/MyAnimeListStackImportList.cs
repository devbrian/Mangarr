using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.MyAnimeListStack
{
    // Quick task 260608-vf9 — "MyAnimeList Stack" ImportList provider.
    //
    // A non-OAuth public scrape: imports manga from a public MyAnimeList Interest-Stack page
    // (https://myanimelist.net/stacks/85344). Extends HttpImportListBase (NOT
    // OAuthAwareImportListBase — there are no tokens here). Auto-discovered via ThingiProvider
    // reflection scan of IMangaImportList — NO manual DI registration.
    //
    // Hard user requirement: the Test() override REJECTS on save when the supplied URL/ID points
    // at an ANIME stack instead of a Manga stack, with a clear actionable message.
    public class MyAnimeListStackImportList : HttpImportListBase<MyAnimeListStackImportListSettings>
    {
        public MyAnimeListStackImportList(IHttpClient httpClient,
                                          IImportListStatusService importListStatusService,
                                          IConfigService configService,
                                          IMangaParsingService parsingService,
                                          ILocalizationService localizationService,
                                          Logger logger)
            : base(httpClient, importListStatusService, configService, parsingService, localizationService, logger)
        {
        }

        public override string Name => "MyAnimeList Stack";

        public override ImportListType ListType => ImportListType.MyAnimeListStack;

        // RESEARCH #5 — mirror the other MAL/AniList import lists (~24h).
        public override TimeSpan MinRefreshInterval => TimeSpan.FromHours(24);

        public override IImportListRequestGenerator GetRequestGenerator()
        {
            return new MyAnimeListStackImportListRequestGenerator { Settings = Settings };
        }

        public override IParseImportListResponse GetParser()
        {
            return new MyAnimeListStackImportListParser();
        }

        // ── Anime-stack guard (the user's hard requirement) ─────────────────────────────────
        // First-class Test() override. Fetches the canonical stack page, parses the manga
        // add-button cards, and counts the anime add-button cards separately. If the page has
        // ZERO manga cards AND >= 1 anime card → it's an Anime stack → REJECT with an actionable
        // message. Zero of either → "no manga found". Otherwise Test passes.
        //
        // Literal English strings for the guard failures, consistent with
        // HttpImportListBase.TestConnection's literal "No results were returned..." convention.
        protected override void Test(List<ValidationFailure> failures)
        {
            IList<ImportListItemInfo> mangaItems;
            int animeCount;

            try
            {
                var request = GetRequestGenerator().GetListItems().GetAllTiers().First().First();
                var httpResponse = _httpClient.Execute(request.HttpRequest);
                var content = httpResponse?.Content ?? string.Empty;

                var response = new ImportListResponse(request, httpResponse);
                mangaItems = GetParser().ParseResponse(response);
                animeCount = MyAnimeListStackImportListParser.CountAnimeItems(content);
            }
            catch (HttpException ex)
            {
                _logger.Warn(ex, "Unable to connect to MyAnimeList stack");
                failures.Add(new NzbDroneValidationFailure(string.Empty,
                    $"Unable to connect to MyAnimeList: {ex.Message}. Check the log surrounding this error for details."));
                return;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to connect to MyAnimeList stack");
                failures.Add(new NzbDroneValidationFailure(string.Empty,
                    $"Unable to connect to MyAnimeList: {ex.Message}. Check the log surrounding this error for details."));
                return;
            }

            if (mangaItems.Count == 0 && animeCount > 0)
            {
                failures.Add(new NzbDroneValidationFailure(nameof(MyAnimeListStackImportListSettings.StackUrl),
                    "This looks like an Anime stack, not a Manga stack. Mangarr only imports manga stacks — browse manga stacks at https://myanimelist.net/stacks/search?type=manga"));
            }
            else if (mangaItems.Count == 0)
            {
                failures.Add(new NzbDroneValidationFailure(nameof(MyAnimeListStackImportListSettings.StackUrl),
                    "No manga found on that page — check the URL points at a public MyAnimeList Manga stack."));
            }
        }
    }
}
