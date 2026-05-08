using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers.Comix;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Indexers.Comix
{
    /// <summary>
    /// Unit fixture for <see cref="ComixHash"/>. Locks the deterministic-output contract:
    /// same input path always produces the same token. Without this guarantee comix.to's
    /// <c>/api/v1/manga/{hid}/chapters?_=&lt;token&gt;</c> auth gate would be flaky.
    ///
    /// Added 2026-05-08 (comix-indexer-404 debug session) when the Hash.kt port from
    /// keiyoushi was introduced.
    /// </summary>
    [TestFixture]
    public class ComixHashFixture : TestBase
    {
        [Test]
        public void GenerateHash_is_deterministic()
        {
            var a = ComixHash.GenerateHash("/manga/mr3m0/chapters");
            var b = ComixHash.GenerateHash("/manga/mr3m0/chapters");

            a.Should().Be(b);
            a.Should().NotBeNullOrWhiteSpace();
        }

        [Test]
        public void GenerateHash_differs_between_paths()
        {
            var a = ComixHash.GenerateHash("/manga/mr3m0/chapters");
            var b = ComixHash.GenerateHash("/manga/gmyj7/chapters");

            a.Should().NotBe(b);
        }

        [Test]
        public void GenerateHash_returns_url_safe_base64()
        {
            // URL-safe base64: '+' replaced with '-', '/' replaced with '_', no padding.
            var token = ComixHash.GenerateHash("/manga/mr3m0/chapters");

            token.Should().NotContain("+");
            token.Should().NotContain("/");
            token.Should().NotContain("=");
        }
    }
}
