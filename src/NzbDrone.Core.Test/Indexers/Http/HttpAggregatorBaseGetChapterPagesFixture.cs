using System;
using System.Net;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers.Http;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Test.IndexerTests;

namespace NzbDrone.Core.Test.Indexers.Http
{
    /// <summary>
    /// Phase 4 plan 04-02 Task 1 — contract test for the new <c>GetChapterPages</c> abstract on
    /// <see cref="HttpAggregatorBase{TSettings}"/> + the <see cref="IHttpAggregator"/> marker
    /// + <see cref="ChapterManifest"/> + <see cref="ChapterPage"/> + <see cref="ManifestExpiredException"/>.
    ///
    /// <para>
    /// Defends against the Phase 3 LEARNINGS surprise "TestHttpAggregator missed by Plan 03-02's
    /// contract fan-out": this fixture FAILS TO COMPILE if the existing
    /// <see cref="TestHttpAggregator"/> helper does not honor the new abstract OR if the
    /// abstract signature drifts from the documented shape.
    /// </para>
    ///
    /// <para>
    /// Pitfall 2 contract guard: <see cref="ManifestExpiredException"/> MUST NOT inherit from
    /// <see cref="NzbDrone.Common.Http.HttpException"/> — Polly's per-page retry pipeline keys
    /// off HttpException as transient and would otherwise swallow the 403/410 → re-fetch path.
    /// </para>
    /// </summary>
    [TestFixture]
    public class HttpAggregatorBaseGetChapterPagesFixture : CoreTest<TestHttpAggregator>
    {
        [Test]
        public void TestHttpAggregator_implements_IHttpAggregator_and_HttpAggregatorBase()
        {
            // Compile-time contract test: this fails to compile if HttpAggregatorBase loses the
            // abstract or if TestHttpAggregator anywhere in the codebase doesn't honor it.
            // Mirrors Phase 3 LEARNINGS "TestHttpAggregator missed by Plan 03-02's contract fan-out".
            Subject.Should().BeAssignableTo<IHttpAggregator>();
            Subject.Should().BeAssignableTo<HttpAggregatorBase<TestHttpAggregatorSettings>>();
        }

        [Test]
        public void ChapterManifest_round_trips_init_only_properties()
        {
            var expires = DateTimeOffset.UtcNow.AddMinutes(15);
            var manifest = new ChapterManifest
            {
                Pages = new[] { new ChapterPage { Url = "https://x/1.png", PageIndex = 1, ContentTypeHint = null } },
                ScanlationGroup = "TestGroup",
                TotalCount = 1,
                ExpiresAt = expires
            };

            manifest.Pages.Should().HaveCount(1);
            manifest.Pages[0].PageIndex.Should().Be(1);
            manifest.Pages[0].Url.Should().Be("https://x/1.png");
            manifest.ScanlationGroup.Should().Be("TestGroup");
            manifest.TotalCount.Should().Be(1);
            manifest.ExpiresAt.Should().Be(expires);
        }

        [Test]
        public void ChapterManifest_default_Pages_is_empty_collection_not_null()
        {
            // Init-only default must not require explicit assignment to avoid null-deref
            // when callers construct an "empty" manifest (e.g., the TestHttpAggregator default).
            var manifest = new ChapterManifest();
            manifest.Pages.Should().NotBeNull();
            manifest.Pages.Should().BeEmpty();
        }

        [Test]
        public void ManifestExpiredException_does_NOT_inherit_HttpException()
        {
            // Pitfall 2 contract test: if ManifestExpiredException ever starts inheriting
            // HttpException, Polly's predicate will swallow 403/410 and the D-03 re-fetch path
            // will silently break.
            var ex = new ManifestExpiredException(5, HttpStatusCode.Forbidden);
            ex.Should().BeOfType<ManifestExpiredException>();
            ex.Should().NotBeAssignableTo<NzbDrone.Common.Http.HttpException>();
            ex.PageIndex.Should().Be(5);
            ex.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Test]
        public void ManifestExpiredException_carries_410_Gone_status()
        {
            // D-03 reactive 403/410 → re-fetch path: both 403 and 410 are signaled via this
            // exception type, distinguished by StatusCode for diagnostic logging.
            var ex = new ManifestExpiredException(7, HttpStatusCode.Gone);
            ex.PageIndex.Should().Be(7);
            ex.StatusCode.Should().Be(HttpStatusCode.Gone);
            ex.Message.Should().Contain("410");
        }

        [Test]
        public void IHttpAggregator_interface_exposes_Phase4_surface()
        {
            // Defensive check: the interface MUST expose SourceKey + ResolveUserAgent +
            // GetDownloadHeaders + GetChapterPages so plan 04-03's ChapterPageFetcher can
            // consume aggregator instances through the non-generic marker.
            var iface = typeof(IHttpAggregator);
            iface.GetProperty("SourceKey").Should().NotBeNull("Phase 1 D-11 contract surface");
            iface.GetMethod("ResolveUserAgent").Should().NotBeNull("Phase 1 D-13 contract surface");
            iface.GetMethod("GetDownloadHeaders").Should().NotBeNull("Phase 3 D-14 contract surface");
            iface.GetMethod("GetChapterPages").Should().NotBeNull("Phase 4 D-01 contract surface");
        }
    }
}
