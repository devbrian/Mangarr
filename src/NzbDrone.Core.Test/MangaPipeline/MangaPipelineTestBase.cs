using System.Collections.Generic;
using DryIoc;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Notifications.Komga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MangaPipeline
{
    // Phase 6 Plan 06-12 — F-01 BLOCKING fixture base. Phase 5 LEARNINGS pattern verbatim
    // (MangaDownloadDecisionMakerEndToEndFixture precedent), extended to include Phase 6
    // services (history, blocklist, queue, notifications).
    //
    // CRITICAL — HTTP-only mocking discipline (HARD rule):
    //   * IHttpClient is the SOLE service-level mock. Every indexer Fetch URL and every
    //     Komga/Kavita scan URL goes through the same Mock<IHttpClient>.
    //   * IIndexer / IIndexerFactory / RequestGenerator / Parser / KomgaProxy /
    //     KavitaProxy are REAL — their auth-header injection, rate-limit middleware, and
    //     wire-shape parsing run against the mocked transport. Mocking them at the service
    //     level would silently undermine the F-01-class regression contract.
    //   * Filesystem operations: a real OS temp directory is used by inheriting from
    //     CoreTest (TempFolder is provided). Mock<IDiskProvider> can be opted into by
    //     subclasses if a destructive operation must be intercepted, but the default is
    //     real disk for the round-trip.
    //
    // The AutoMoqer container is a real DryIoc IContainer — concrete classes are
    // constructed via DryIoc's reflection-based ConstructorWithResolvableArgumentsIncludingNonPublic
    // factory method, and unknown interfaces fall back to Moq mocks. We therefore explicitly
    // SetConstant the few concrete services this fixture's assertions exercise so that
    // Container.Resolve<T> returns a real instance rather than a Moq.
    //
    // The Container property surfaces the DryIoc container directly so subclasses can
    // call Container.Resolve<T>() in addition to Mocker.Resolve<T>().
    public abstract class MangaPipelineTestBase : DbTest
    {
        protected IContainer Container => Mocker.Container;
        protected Mock<IHttpClient> HttpClientMock { get; private set; }

        // Default seed values — subclasses can override SeedDefaults() or use the helpers.
        protected NzbDrone.Core.Manga.Manga SeededManga { get; private set; }
        protected NzbDrone.Core.Manga.Chapter SeededChapter { get; private set; }
        protected NotificationDefinition SeededKomgaDefinition { get; private set; }

        [SetUp]
        public void PipelineSetup()
        {
            // ── HTTP boundary mock — the SOLE service-level mock for the F-01 path ─────
            HttpClientMock = Mocker.GetMock<IHttpClient>();

            // Default: any unmatched HTTP request returns a 200 OK with empty body so the
            // real proxies / indexers don't NRE on a null response. Specific tests override
            // by setting up matching predicates (URL contains /scan, etc.) that take
            // precedence under Moq's matcher resolution.
            HttpClientMock
                .Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(req => new HttpResponse(req, new HttpHeader(), new byte[0], System.Net.HttpStatusCode.OK));

            // ── Register REAL Phase 6 concrete services on the DI container ────────────
            // The AutoMoqer DryIoc container will reflectively construct these on first
            // resolve; SetConstant + a hot Resolve guarantees the same instance every time.
            // Required Mocker.Resolve<T>() warmup happens on demand; subclasses that need
            // a particular service eagerly should call Container.Resolve<T>() in [SetUp].

            // Cache manager — Mocker provides real one by default (see TestBase.Mocker).
            // Logger — Mocker provides real one by default (TestLogger).

            // Seed default entities used by most tests.
            SeedDefaults();
        }

        // Default seed: 1 Manga + 1 Chapter + 1 KomgaNotification definition. Subclasses
        // override to alter / extend.
        protected virtual void SeedDefaults()
        {
            SeededManga = new NzbDrone.Core.Manga.Manga
            {
                Id = 1,
                Title = "Test Manga",
                CleanTitle = "testmanga",
                Path = System.IO.Path.Combine(TempFolder, "Test Manga"),
                Monitored = true,
                TranslationProfileId = 1,
                CustomFormatProfileId = 1
            };

            SeededChapter = new NzbDrone.Core.Manga.Chapter
            {
                Id = 100,
                MangaId = 1,
                ChapterNumber = 1m,
                Title = "Chapter 1",
                Monitored = true
            };

            SeededKomgaDefinition = new NotificationDefinition
            {
                Id = 1,
                Name = "Komga (Test)",
                ConfigContract = nameof(KomgaNotificationSettings),
                Implementation = nameof(KomgaNotification),
                Settings = new KomgaNotificationSettings
                {
                    Url = "http://komga.local:25600",
                    ApiKey = "test-key",
                    LibraryId = 42
                },
                OnChapterImport = true,
                Tags = new HashSet<int>()
            };
        }

        // Helper: stub a JSON HTTP response for any request whose URL contains the given
        // substring. Used by F-01 tests to script indexer-fetch responses that the REAL
        // indexer providers parse and the rest of the pipeline consumes.
        protected void StubHttpJson(string urlContains, string jsonBody, System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK)
        {
            HttpClientMock
                .Setup(c => c.Execute(It.Is<HttpRequest>(r => r.Url.ToString().Contains(urlContains))))
                .Returns<HttpRequest>(req => new HttpResponse(req, new HttpHeader(), jsonBody, status));
        }

        // Helper: assert exactly N HTTP requests went out matching the URL substring. Used
        // by F-01 tests to assert Komga rescan POST landed.
        protected void VerifyHttpCall(string urlContains, Times times)
        {
            HttpClientMock.Verify(
                c => c.Execute(It.Is<HttpRequest>(r => r.Url.ToString().Contains(urlContains))),
                times);
        }
    }
}
