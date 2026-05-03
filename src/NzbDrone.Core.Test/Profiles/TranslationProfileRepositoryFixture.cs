using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Profiles.Translations;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Profiles
{
    // DbTest verifies the StringListConverter<List<string>> JSON round-trip per D-03.
    // PATTERNS-MAP Adaptation Hotspot 8: converter ALREADY registered at TableMapping.cs:233 — no new registration needed.
    [TestFixture]
    public class TranslationProfileRepositoryFixture : DbTest<TranslationProfileRepository, TranslationProfile>
    {
        [Test]
        public void Languages_list_round_trip_persists_via_StringListConverter()
        {
            var profile = new TranslationProfile
            {
                Name = "EnEsRaw",
                Languages = new List<string> { "en", "es", "raw" },
                AllowLanguagesNotInProfile = false
            };

            Subject.Insert(profile);
            var loaded = Subject.Get(profile.Id);

            loaded.Languages.Should().Equal("en", "es", "raw");
            loaded.AllowLanguagesNotInProfile.Should().BeFalse();
        }

        [Test]
        public void Exists_returns_true_for_known_id()
        {
            var profile = new TranslationProfile
            {
                Name = "X",
                Languages = new List<string> { "en" }
            };

            Subject.Insert(profile);

            Subject.Exists(profile.Id).Should().BeTrue();
            Subject.Exists(profile.Id + 9999).Should().BeFalse();
        }
    }
}
