using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers.MangaDex;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers
{
    /// <summary>
    /// WR-04 (#147) NRE-guard fixture for <c>HttpAggregatorBase&lt;TSettings&gt;</c>.
    ///
    /// Before this guard, <c>SourceKey</c>, <c>RateLimit</c>, and <c>ResolveUserAgent()</c>
    /// dereferenced <c>Definition.Settings</c> directly — which throws
    /// <see cref="System.NullReferenceException"/> when the provider is exercised before its
    /// <c>Definition</c> is wired (e.g. during DI auto-discovery probing or test harness
    /// construction). The fix routes the three accessors through a <c>SettingsOrDefault</c>
    /// helper that falls back to a fresh <c>TSettings</c> instance.
    ///
    /// Uses <see cref="MangaDexIndexer"/> as a concrete <c>HttpAggregatorBase&lt;TSettings&gt;</c>
    /// subject — we intentionally do NOT call <c>Subject.Definition = ...</c> so the unwired
    /// path is exercised end-to-end.
    /// </summary>
    [TestFixture]
    public class HttpAggregatorBaseNreGuardFixture : CoreTest<MangaDexIndexer>
    {
        // Intentionally NO [SetUp] wiring Definition — we want the unwired path under test.

        [Test]
        public void SourceKey_should_not_throw_when_Definition_is_null()
        {
            // Falls back to DefaultSourceKey because SettingsOrDefault.SourceKey is null/empty.
            string key = null;
            FluentActions.Invoking(() => key = Subject.SourceKey).Should().NotThrow();
            key.Should().Be(Subject.DefaultSourceKey);
        }

        [Test]
        public void RateLimit_should_not_throw_when_Definition_is_null()
        {
            // Falls back to base.RateLimit because SettingsOrDefault.Rate is null.
            FluentActions.Invoking(() => _ = Subject.RateLimit).Should().NotThrow();
        }

        [Test]
        public void ResolveUserAgent_should_not_throw_when_Definition_is_null()
        {
            // Falls back to BuildUserAgent() because SettingsOrDefault.UserAgentOverride is null/empty.
            string ua = null;
            FluentActions.Invoking(() => ua = Subject.ResolveUserAgent()).Should().NotThrow();
            ua.Should().StartWith("Mangarr/");
        }
    }
}
