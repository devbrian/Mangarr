using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.CustomFormats.Events;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.CustomFormats;
using NzbDrone.Core.Test.Framework;
using MangaModel = NzbDrone.Core.Manga.Manga;

namespace NzbDrone.Core.Test.Profiles
{
    [TestFixture]
    public class CustomFormatProfileServiceFixture : CoreTest<CustomFormatProfileService>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IMangaService>()
                  .Setup(s => s.GetAllManga())
                  .Returns(new List<MangaModel>());

            Mocker.GetMock<ICustomFormatService>()
                  .Setup(s => s.All())
                  .Returns(new List<CustomFormat>());
        }

        [Test]
        public void init_should_add_default_profile_with_no_score_gate()
        {
            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<ICustomFormatProfileRepository>().Verify(
                v => v.Insert(It.Is<CustomFormatProfile>(p =>
                    p.Name == "Default" &&
                    p.MinFormatScore == 0 &&
                    p.MaxFormatScore == null)),
                Times.Once());
        }

        [Test]
        public void init_should_pre_populate_FormatItems_with_every_existing_CF_at_score_zero()
        {
            var cfs = Builder<CustomFormat>.CreateListOfSize(3).Build().ToList();
            Mocker.GetMock<ICustomFormatService>().Setup(s => s.All()).Returns(cfs);

            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<ICustomFormatProfileRepository>().Verify(
                v => v.Insert(It.Is<CustomFormatProfile>(p =>
                    p.FormatItems.Count == 3 &&
                    p.FormatItems.All(fi => fi.Score == 0))),
                Times.Once());
        }

        [Test]
        public void init_should_set_global_default_to_seeded_profile_id()
        {
            Mocker.GetMock<ICustomFormatProfileRepository>()
                  .Setup(r => r.Insert(It.IsAny<CustomFormatProfile>()))
                  .Returns<CustomFormatProfile>(p =>
                  {
                      p.Id = 11;
                      return p;
                  });

            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<IConfigService>().VerifySet(c => c.DefaultCustomFormatProfileId = 11, Times.Once());
        }

        [Test]
        public void init_should_skip_if_any_profiles_already_exist()
        {
            Mocker.GetMock<ICustomFormatProfileRepository>()
                  .Setup(s => s.All())
                  .Returns(Builder<CustomFormatProfile>.CreateListOfSize(1).Build().ToList());

            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<ICustomFormatProfileRepository>()
                  .Verify(v => v.Insert(It.IsAny<CustomFormatProfile>()), Times.Never());
        }

        [Test]
        public void Handle_CustomFormatAddedEvent_inserts_zero_score_FormatItem_into_every_profile()
        {
            var existing = new CustomFormatProfile
            {
                Id = 1,
                Name = "P1",
                FormatItems = new List<ProfileFormatItem>()
            };

            Mocker.GetMock<ICustomFormatProfileRepository>()
                  .Setup(r => r.All())
                  .Returns(new List<CustomFormatProfile> { existing });

            var newCf = new CustomFormat { Id = 99, Name = "NewCF" };

            Subject.Handle(new CustomFormatAddedEvent(newCf));

            Mocker.GetMock<ICustomFormatProfileRepository>().Verify(
                v => v.Update(It.Is<CustomFormatProfile>(p =>
                    p.FormatItems.Any(fi => fi.Format.Id == 99 && fi.Score == 0))),
                Times.Once());
        }

        [Test]
        public void Handle_CustomFormatDeletedEvent_removes_FormatItem_from_every_profile()
        {
            var doomed = new CustomFormat { Id = 5 };
            var existing = new CustomFormatProfile
            {
                Id = 1,
                Name = "P1",
                FormatItems = new List<ProfileFormatItem>
                {
                    new ProfileFormatItem { Format = doomed, Score = 100 },
                    new ProfileFormatItem { Format = new CustomFormat { Id = 6 }, Score = 50 }
                }
            };

            Mocker.GetMock<ICustomFormatProfileRepository>()
                  .Setup(r => r.All())
                  .Returns(new List<CustomFormatProfile> { existing });

            Subject.Handle(new CustomFormatDeletedEvent(doomed));

            Mocker.GetMock<ICustomFormatProfileRepository>().Verify(
                v => v.Update(It.Is<CustomFormatProfile>(p =>
                    p.FormatItems.Count == 1 &&
                    p.FormatItems.All(fi => fi.Format.Id != 5))),
                Times.Once());
        }

        [Test]
        public void delete_should_throw_when_profile_assigned_to_any_manga()
        {
            Mocker.GetMock<IMangaService>()
                  .Setup(s => s.GetAllManga())
                  .Returns(new List<MangaModel>
                  {
                      new MangaModel { Id = 1, CustomFormatProfileId = 42 }
                  });

            Mocker.GetMock<ICustomFormatProfileRepository>()
                  .Setup(r => r.Get(42))
                  .Returns(new CustomFormatProfile { Id = 42, Name = "P" });

            Assert.Throws<CustomFormatProfileInUseException>(() => Subject.Delete(42));

            Mocker.GetMock<ICustomFormatProfileRepository>()
                  .Verify(r => r.Delete(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void delete_should_throw_when_profile_is_global_default()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.DefaultCustomFormatProfileId).Returns(99);
            Mocker.GetMock<ICustomFormatProfileRepository>()
                  .Setup(r => r.Get(99))
                  .Returns(new CustomFormatProfile { Id = 99, Name = "Default" });

            Assert.Throws<CustomFormatProfileInUseException>(() => Subject.Delete(99));

            Mocker.GetMock<ICustomFormatProfileRepository>()
                  .Verify(r => r.Delete(It.IsAny<int>()), Times.Never());
        }
    }
}
