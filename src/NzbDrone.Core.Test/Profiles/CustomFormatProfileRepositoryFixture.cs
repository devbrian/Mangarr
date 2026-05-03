using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.CustomFormats;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Profiles
{
    [TestFixture]
    public class CustomFormatProfileRepositoryFixture : DbTest<CustomFormatProfileRepository, CustomFormatProfile>
    {
        [Test]
        public void FormatItems_round_trip_persists_via_EmbeddedDocumentConverter()
        {
            var profile = new CustomFormatProfile
            {
                Name = "FmtTest",
                MinFormatScore = 10,
                MaxFormatScore = 500,
                FormatItems = new List<ProfileFormatItem>
                {
                    new ProfileFormatItem { Format = new CustomFormat { Id = 1, Name = "X" }, Score = 100 },
                    new ProfileFormatItem { Format = new CustomFormat { Id = 2, Name = "Y" }, Score = -50 }
                }
            };

            Subject.Insert(profile);
            var loaded = Subject.Get(profile.Id);

            loaded.MinFormatScore.Should().Be(10);
            loaded.MaxFormatScore.Should().Be(500);
            loaded.FormatItems.Should().HaveCount(2);
            loaded.FormatItems[0].Score.Should().Be(100);
        }

        [Test]
        public void MaxFormatScore_null_round_trip()
        {
            var profile = new CustomFormatProfile
            {
                Name = "NullCap",
                MinFormatScore = 0,
                MaxFormatScore = null
            };

            Subject.Insert(profile);
            var loaded = Subject.Get(profile.Id);

            loaded.MaxFormatScore.Should().BeNull();
        }
    }
}
