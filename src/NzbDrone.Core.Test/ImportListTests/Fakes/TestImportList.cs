using System;
using System.Collections.Generic;
using FluentValidation.Results;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser.Manga;

namespace NzbDrone.Core.Test.ImportListTests.Fakes
{
    // Phase 26 Plan 26-04 (D-09 + Pitfall 2) — test-only fake provider. MUST live in
    // NzbDrone.Core.Test (this assembly) so the production reflection-scan does NOT see
    // it; the bucket A SC#6 anti-prod-leak gate
    // (ImportListFactoryFixture.factory_returns_zero_providers_on_empty_di_bag) is the
    // static enforcement that this fake never leaks into Mangarr.Core.dll's DI graph.
    //
    // Fetch() returns a deterministic 3-item ImportListFetchResult so bucket B fixtures
    // can assert exact rows survive the dedup + exclusion + already-in-library pipeline.
    public class TestImportList : ImportListBase<TestImportListSettings>
    {
        public TestImportList(IImportListStatusService importListStatusService, IConfigService configService, IMangaParsingService parsingService, ILocalizationService localizationService, Logger logger)
            : base(importListStatusService, configService, parsingService, localizationService, logger)
        {
        }

        public override string Name => "TestImportList";

        public override ImportListType ListType => ImportListType.Other;

        public override TimeSpan MinRefreshInterval => TimeSpan.FromHours(24);

        public override ImportListFetchResult Fetch()
        {
            // Deterministic 3-item payload — stable Guid strings so fixtures can match.
            var items = new List<ImportListItemInfo>
            {
                new ImportListItemInfo
                {
                    Title = "Test Manga 1",
                    MangaDexId = "11111111-1111-1111-1111-111111111111",
                    ReleaseDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                new ImportListItemInfo
                {
                    Title = "Test Manga 2",
                    MangaDexId = "22222222-2222-2222-2222-222222222222",
                    AniListId = 42,
                    ReleaseDate = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                new ImportListItemInfo
                {
                    Title = "Test Manga 3",
                    MalId = 99,
                    ReleaseDate = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc)
                }
            };

            return new ImportListFetchResult(CleanupListItems(items), anyFailure: false);
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            // No remote endpoint to test in the fake — always succeeds.
        }
    }
}
