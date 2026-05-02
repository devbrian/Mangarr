using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.MyAnimeList
{
    // Wave 0 scaffold for MyAnimeListMetadataSource (D-24 — official MAL v2,
    // client-ID-only auth). RED until Plan 02-08.
    //
    // Production-shape this fixture will exercise once 02-08 lands:
    //   public class MyAnimeListMetadataSourceFixture : CoreTest<MyAnimeListMetadataSource>
    //
    // Acceptance anchor (Plan 02-01): every outbound HttpRequest from this provider
    // MUST carry the literal header "X-MAL-CLIENT-ID" (D-24 + threat T-CRED-01).
    [TestFixture]
    public class MyAnimeListMetadataSourceFixture : CoreTest
    {
        // The actual outbound HttpRequest must carry Headers["X-MAL-CLIENT-ID"].
        [Test]
        [Ignore("RED — Plan 02-08 lands the X-MAL-CLIENT-ID header injection.")]
        public void GetMangaInfo_includes_X_MAL_CLIENT_ID_header()
            => Assert.Inconclusive("Plan 02-08 — header literal: X-MAL-CLIENT-ID");

        [Test]
        [Ignore("RED — Plan 02-08 lands the authors[] role=Story extraction.")]
        public void GetMangaInfo_extracts_primary_author_from_authors_role_Story()
            => Assert.Inconclusive("Plan 02-08");

        // MAL v2 sparse-by-default — must declare required fields per RESEARCH §Code Examples
        // Pattern 4 line 1120.
        [Test]
        [Ignore("RED — Plan 02-08 lands the fields query-param injector.")]
        public void Search_includes_required_fields_query_param()
            => Assert.Inconclusive("Plan 02-08");
    }
}
