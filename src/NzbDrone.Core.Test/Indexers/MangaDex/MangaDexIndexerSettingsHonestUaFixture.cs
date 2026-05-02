using System.Reflection;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Indexers.Http;
using NzbDrone.Core.Indexers.MangaDex;

namespace NzbDrone.Core.Test.Indexers.MangaDex
{
    /// <summary>
    /// Wave 0 reflection assertion realizing T-CONFIG-DRIFT-01 mitigation by ENFORCEMENT
    /// (Pitfall 4 in 03-RESEARCH.md). The MangaDex ToS REQUIRES the honest "Mangarr/{version}" UA.
    /// We implement <see cref="IHttpAggregatorSettings.UserAgentOverride"/> on
    /// <see cref="MangaDexIndexerSettings"/> because the interface contract requires it, but we
    /// MUST NOT decorate the property with <see cref="FieldDefinitionAttribute"/> — adding it
    /// would expose the override field in Settings UI and let users spoof the UA, leading to a
    /// ToS ban.
    ///
    /// This test is the compile/test-time gate that prevents future contributors from
    /// accidentally regressing. Mirrors the same pattern verified in Phase 2 against
    /// <c>MangaDexMetadataSourceSettings</c> (where the property is also intentionally
    /// undecorated).
    ///
    /// References NOT-YET-BUILT type <see cref="MangaDexIndexerSettings"/> (lands in Plan 03-04).
    /// </summary>
    [TestFixture]
    public class MangaDexIndexerSettingsHonestUaFixture
    {
        [Test]
        public void UserAgentOverride_must_have_no_FieldDefinition_attribute()
        {
            var prop = typeof(MangaDexIndexerSettings).GetProperty(
                nameof(IHttpAggregatorSettings.UserAgentOverride),
                BindingFlags.Public | BindingFlags.Instance);

            prop.Should().NotBeNull(
                "UserAgentOverride is required by the IHttpAggregatorSettings interface contract.");

            var attrs = prop!.GetCustomAttributes(typeof(FieldDefinitionAttribute), inherit: true);
            attrs.Should().BeEmpty(
                "MangaDex ToS REQUIRES the honest 'Mangarr/{version}' UA — see Pitfall 4 / "
                + "T-CONFIG-DRIFT-01. Adding [FieldDefinition] would expose the override field in "
                + "the Settings UI and let users spoof, leading to ToS ban.");
        }
    }
}
