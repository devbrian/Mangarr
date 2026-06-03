using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Xml.Linq;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients.Gateway;
using NzbDrone.Core.Download.Clients.Gateway.Responses;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Gateway;
using NzbDrone.Core.Indexers.Gateway.Responses;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.ChapterArchiving.Metadata.ComicInfo;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.Clients.Gateway
{
    /// <summary>
    /// Phase 38 Plan 38-03 Task 1 — HONEST end-to-end acceptance fixture (GWDL-01..04 + CINFO-01
    /// composed). Proves the two halves of the phase interoperate WITHOUT synthetic shortcuts:
    ///
    ///   SEARCH  — drive the REAL Phase-37 <see cref="GatewayParser"/> against a stubbed
    ///             <c>POST /search</c> body and capture the gateway-minted <c>downloadHandle</c>
    ///             (R6) as <see cref="ReleaseInfo.DownloadUrl"/> (GatewayParser.cs:80). No literal
    ///             handle is constructed in the test — the handle is read out of the search result.
    ///   GRAB    — feed the captured release to the REAL <see cref="GatewayDownloadClient.Download"/>,
    ///             stubbing at the <see cref="IHttpClient"/> layer so the REAL
    ///             <see cref="GatewayDownloadProxy"/> serialises the submit body + deserialises the
    ///             response. Assert the handle submitted to <c>POST /downloads</c> EQUALS the handle
    ///             captured from search (the honest-e2e guardrail, T-38-03-01), and that the stubbed
    ///             <c>{jobId:"job-1"}</c> is returned as the DownloadId.
    ///   POLL    — stub <c>GET /downloads</c> to return the job <c>completed</c> with an
    ///             <c>outputPath</c> pointing at a REAL 3-image temp <c>.cbz</c>; assert the client
    ///             maps it to <see cref="DownloadItemStatus.Completed"/> with the identity-remapped
    ///             OutputPath.
    ///   IMPORT  — run the REAL <see cref="ComicInfoCbzInjector"/> (the step-3.5 injector wired into
    ///             ImportApprovedChapters by Plan 38-02) over the completed CBZ, then re-open the
    ///             archive and assert a <c>ComicInfo.xml</c> entry carries the grabbed release's
    ///             ScanlationGroup + TranslatedLanguage + <c>&lt;PageCount&gt;3&lt;/PageCount&gt;</c>.
    ///
    /// SCOPING DECISION (documented per the plan's escape hatch): the named integration-harness path
    /// <c>NzbDrone.Integration.Test/Gateway/</c> CANNOT host this fixture — that project references
    /// only <c>Mangarr.Test.Common</c> + <c>Mangarr.Http</c> and has NO reference to
    /// <c>NzbDrone.Core</c>, so it cannot see <see cref="GatewayParser"/>, the R6 handle types, or
    /// <see cref="GatewayDownloadClient"/>. Per RESEARCH "Environment Availability" a live gateway is
    /// also unavailable. The fixture therefore lands in <c>NzbDrone.Core.Test</c> (the only project
    /// with the Core reference) and stubs at the REAL HTTP boundary (<see cref="IHttpClient"/>) — NOT
    /// at the <see cref="IGatewayDownloadProxy"/> seam — so the proxy's real serialisation +
    /// deserialisation + 400/404 handling run. It drives the REAL parser → REAL client → REAL
    /// injector against the REAL R6 handle (the non-negotiable: handle flows search→grab unmodified,
    /// imported CBZ has injected ComicInfo). It runs headless in CI, so it is NOT marked [Explicit].
    /// </summary>
    [TestFixture]
    public class GatewayGrabImportAcceptanceFixture : CoreTest<GatewayDownloadClient>
    {
        private const string ScanlationGroup = "Flame-Scans";
        private const string TranslatedLanguage = "en";

        private string _tempDir;
        private GatewayDownloadClientSettings _settings;

        // The submit body the client/proxy actually serialised over POST /downloads — captured at the
        // HTTP boundary so the assertion proves no fabrication slipped in.
        private GatewaySubmitRequest _submitted;

        [SetUp]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "GatewayGrabImport_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            _settings = new GatewayDownloadClientSettings
            {
                Host = "gateway-host",
                Port = 9191,
                ApiKey = "test-api-key"
            };

            // Identity remap — the localhost-remap = identity contract (step 3 of the plan).
            Mocker.GetMock<IRemotePathMappingService>()
                  .Setup(s => s.RemapRemoteToLocal(It.IsAny<string>(), It.IsAny<NzbDrone.Common.Disk.OsPath>()))
                  .Returns<string, NzbDrone.Common.Disk.OsPath>((_, p) => p);

            // Honest-e2e: register the REAL GatewayDownloadProxy (NOT an auto-mocked IGatewayDownloadProxy
            // seam) so the proxy's real serialise/deserialise + 400/404 handling run. It is wired to the
            // mock IHttpClient — the stub lives at the HTTP boundary, one layer below the proxy. MUST be
            // set BEFORE the first Subject resolution so the client gets the real proxy, not an auto-mock.
            Mocker.SetConstant<IGatewayDownloadProxy>(
                new GatewayDownloadProxy(Mocker.GetMock<IHttpClient>().Object, TestLogger));

            Subject.Definition = new DownloadClientDefinition
            {
                Id = 1,
                Name = "Manga Gateway",
                Settings = _settings,
                ConfigContract = nameof(GatewayDownloadClientSettings)
            };
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }

        [Test]
        public void search_grab_import_uses_real_R6_handle_and_injects_comicinfo()
        {
            // ── 1. SEARCH ── drive the REAL GatewayParser over a stubbed /search body. The gateway
            // mints `downloadHandle`; the parser maps it to ReleaseInfo.DownloadUrl (GatewayParser.cs:80).
            // We capture THIS value — no handle literal is passed to Download independent of search.
            var searchedRelease = RunRealSearch();
            var searchedHandle = searchedRelease.DownloadUrl;

            searchedHandle.Should().NotBeNullOrEmpty("the gateway-minted R6 handle must survive the parse");
            searchedRelease.ScanlationGroup.Should().Be(ScanlationGroup);
            searchedRelease.TranslatedLanguage.Should().Be(TranslatedLanguage);

            // A real, completed CBZ that POST/GET will report as the job output (3 pages, no ComicInfo).
            var outputCbz = MakePageOnlyCbz(3);

            // ── 2. GRAB ── stub the HTTP boundary so the REAL proxy serialises the submit + parses the
            // response. POST /downloads → {jobId:"job-1"}; GET /downloads → the job completed at outputCbz.
            StubGatewayHttp(outputCbz);

            var grabbedRelease = searchedRelease;   // the SAME object from search — no rebuild
            var remoteChapter = new RemoteChapter { Release = grabbedRelease };

            var downloadId = Subject.Download(remoteChapter, Mocker.GetMock<IIndexer>().Object)
                                    .GetAwaiter().GetResult();

            // BEHAVIOR: the stubbed jobId is the DownloadId.
            downloadId.Should().Be("job-1");

            // BEHAVIOR (T-38-03-01 honest-e2e guardrail): the handle the proxy actually submitted to
            // POST /downloads EQUALS the handle the search produced. No synthetic handle.
            _submitted.Should().NotBeNull("the proxy must have serialised a submit body to POST /downloads");
            _submitted.DownloadUrl.Should().Be(searchedHandle,
                "the grab must consume the EXACT R6 handle minted by search — no fabrication");
            _submitted.ReleaseHandle.Should().Be(grabbedRelease.Guid);

            // ── 3. POLL/COMPLETE ── the client maps the GET /downloads job to Completed and resolves
            // the OutputPath (identity remap).
            var item = Subject.GetItems().Single(i => i.DownloadId == "job-1");
            item.Status.Should().Be(DownloadItemStatus.Completed);
            item.OutputPath.FullPath.Should().Be(new NzbDrone.Common.Disk.OsPath(outputCbz).FullPath);
            File.Exists(item.OutputPath.FullPath).Should().BeTrue("the completed CBZ must be a real file on disk");

            // ── 4. IMPORT + INJECT ── reconstruct the persisted ChapterFile the import pipeline builds
            // (ScanlationGroup + TranslatedLanguage carried from the grabbed release — D-C2), then run
            // the REAL injector (the step-3.5 wire of Plan 38-02) over the completed CBZ.
            var manga = new NzbDrone.Core.Manga.Manga { Id = 7, Title = "Solo Leveling" };
            var chapter = new Chapter { Id = 42, MangaId = 7, Title = "Chapter 179", ChapterNumber = 179m };

            var chapterFile = new ChapterFile
            {
                Id = 99,
                MangaId = manga.Id,
                ChapterId = chapter.Id,
                Path = item.OutputPath.FullPath,
                ScanlationGroup = grabbedRelease.ScanlationGroup,        // grabbed metadata, not a literal
                TranslatedLanguage = grabbedRelease.TranslatedLanguage
            };

            var injector = new ComicInfoCbzInjector(TestLogger);
            injector.Inject(chapterFile, manga, chapter);

            // ── ASSERT ── re-open the imported CBZ; a ComicInfo.xml entry carries the grabbed
            // ScanlationGroup + TranslatedLanguage + the REAL page count (3).
            using var zip = ZipFile.OpenRead(item.OutputPath.FullPath);
            var entry = zip.GetEntry("ComicInfo.xml");
            entry.Should().NotBeNull("the import must upsert a ComicInfo.xml entry into the delivered CBZ");

            using var stream = entry.Open();
            var doc = XDocument.Load(stream);

            doc.Root!.Element("PageCount").Value.Should().Be("3", "the injector counts the 3 real page entries");
            doc.Root.Element("Translator").Value.Should().Be(ScanlationGroup, "v2.1 scanlation group (grabbed)");
            doc.Root.Element("ScanInformation").Value.Should().Be(ScanlationGroup, "v2.0 scanlation group (grabbed)");
            doc.Root.Element("LanguageISO").Value.Should().Be(TranslatedLanguage, "the grabbed translated language");
            doc.Root.Element("Series").Value.Should().Be("Solo Leveling");
        }

        // Drives the REAL GatewayParser against a stubbed POST /search body and returns the single
        // release. The downloadHandle here is the ONLY source of the handle used downstream.
        private ReleaseInfo RunRealSearch()
        {
            var searchBody = JsonConvert.SerializeObject(new GatewaySearchResponse
            {
                Releases = new List<GatewayRelease>
                {
                    new GatewayRelease
                    {
                        Guid = "comix.to:solo-leveling:179",
                        Title = "Solo Leveling - Chapter 179",
                        SourceKey = "comix.to",

                        // The gateway-minted opaque R6 token — the value the grab MUST consume verbatim.
                        DownloadHandle = "gw-handle-" + Guid.NewGuid().ToString("N"),

                        MangaTitle = "Solo Leveling",
                        ChapterNumber = 179m,
                        Language = TranslatedLanguage,
                        ScanlationGroup = ScanlationGroup,
                        PageCount = 3,
                        PublishDate = DateTime.UtcNow
                    }
                }
            });

            var parser = new GatewayParser(Mocker.GetMock<IIndexerSourceStatusService>().Object);

            var httpRequest = new HttpRequest("http://gateway-host:9191/search");
            var httpResponse = new HttpResponse(httpRequest, new HttpHeader(), searchBody, HttpStatusCode.OK);
            var indexerResponse = new IndexerResponse(new IndexerRequest(httpRequest), httpResponse);

            var releases = parser.ParseResponse(indexerResponse);
            releases.Should().HaveCount(1, "the stubbed /search body carries exactly one release");

            return releases.Single();
        }

        // Stubs the IHttpClient HTTP boundary (NOT the IGatewayDownloadProxy seam) so the REAL proxy
        // serialisation/deserialisation runs. Captures the submit body so the handle equality can be
        // asserted, and reports the job as completed at the supplied output CBZ.
        private void StubGatewayHttp(string outputCbz)
        {
            var http = Mocker.GetMock<IHttpClient>();

            // POST /downloads — capture the body the REAL proxy serialised (camelCase via the
            // project Json serializer), return the gateway-shaped {jobId:"job-1"} 200 body.
            http.Setup(c => c.Post(It.Is<HttpRequest>(r => r.Url.Path.EndsWith("/downloads"))))
                .Returns<HttpRequest>(req =>
                {
                    var submittedJson = System.Text.Encoding.UTF8.GetString(req.ContentData);
                    _submitted = NzbDrone.Common.Serializer.Json.Deserialize<GatewaySubmitRequest>(submittedJson);
                    var body = NzbDrone.Common.Serializer.Json.ToJson(new GatewaySubmitResponse { JobId = "job-1", Status = "queued" });
                    return new HttpResponse(req, new HttpHeader(), body, HttpStatusCode.OK);
                });

            // GET /downloads — the job completed, output at the real temp CBZ.
            http.Setup(c => c.Get(It.Is<HttpRequest>(r => r.Url.Path.EndsWith("/downloads"))))
                .Returns<HttpRequest>(req =>
                {
                    var jobs = new GatewayJobList
                    {
                        Jobs = new List<GatewayJob>
                        {
                            new GatewayJob
                            {
                                JobId = "job-1",
                                Title = "Solo Leveling - Chapter 179",
                                Status = "completed",
                                OutputPath = outputCbz
                            }
                        }
                    };
                    return new HttpResponse(req, new HttpHeader(), NzbDrone.Common.Serializer.Json.ToJson(jobs), HttpStatusCode.OK);
                });
        }

        // A real, page-only CBZ (no ComicInfo.xml) — the gateway-delivered shape the injector upserts into.
        private string MakePageOnlyCbz(int pageCount)
        {
            var path = Path.Combine(_tempDir, "ch" + Guid.NewGuid().ToString("N") + ".cbz");
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                for (var i = 0; i < pageCount; i++)
                {
                    var entry = zip.CreateEntry($"{i:D4}.jpg", CompressionLevel.NoCompression);
                    using var s = entry.Open();
                    var bytes = new byte[] { 0xFF, 0xD8, 0xFF, (byte)i };  // fake JPEG-ish header
                    s.Write(bytes, 0, bytes.Length);
                }
            }

            return path;
        }
    }
}
