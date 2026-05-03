using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Profiles.Translations;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Profiles
{
    [TestFixture]
    public class TranslationProfileServiceFixture : CoreTest<TranslationProfileService>
    {
        [SetUp]
        public void Setup()
        {
            // Default: no Manga in DB, repository.All() returns empty.
            Mocker.GetMock<IMangaService>()
                  .Setup(s => s.GetAllManga())
                  .Returns(new List<NzbDrone.Core.Manga.Manga>());

            Mocker.GetMock<ITranslationProfileRepository>()
                  .Setup(r => r.All())
                  .Returns(new List<TranslationProfile>());

            // Default Insert returns the profile back (no Id assignment unless overridden in a test).
            Mocker.GetMock<ITranslationProfileRepository>()
                  .Setup(r => r.Insert(It.IsAny<TranslationProfile>()))
                  .Returns<TranslationProfile>(p => p);
        }

        [Test]
        public void init_should_add_default_English_Only_profile()
        {
            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<ITranslationProfileRepository>().Verify(
                v => v.Insert(It.Is<TranslationProfile>(p =>
                    p.Name == "English Only" &&
                    p.Languages.Count == 1 &&
                    p.Languages[0] == "en" &&
                    p.AllowLanguagesNotInProfile == false)),
                Times.Once());
        }

        [Test]
        public void init_should_set_global_default_to_seeded_profile_id()
        {
            // Override Insert to assign Id=7 on the seeded profile so the service
            // can read profile.Id back.
            Mocker.GetMock<ITranslationProfileRepository>()
                  .Setup(r => r.Insert(It.IsAny<TranslationProfile>()))
                  .Returns<TranslationProfile>(p =>
                  {
                      p.Id = 7;
                      return p;
                  });

            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<IConfigService>()
                  .VerifySet(c => c.DefaultTranslationProfileId = 7, Times.Once());
        }

        [Test]
        public void init_should_skip_if_any_profiles_already_exist()
        {
            Mocker.GetMock<ITranslationProfileRepository>()
                  .Setup(s => s.All())
                  .Returns(Builder<TranslationProfile>.CreateListOfSize(1).Build().ToList());

            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<ITranslationProfileRepository>()
                  .Verify(v => v.Insert(It.IsAny<TranslationProfile>()), Times.Never());
        }

        [Test]
        public void delete_should_throw_when_profile_assigned_to_any_manga()
        {
            Mocker.GetMock<IMangaService>()
                  .Setup(s => s.GetAllManga())
                  .Returns(new List<NzbDrone.Core.Manga.Manga>
                  {
                      new NzbDrone.Core.Manga.Manga { Id = 1, TranslationProfileId = 42 }
                  });

            Mocker.GetMock<ITranslationProfileRepository>()
                  .Setup(r => r.Get(42))
                  .Returns(new TranslationProfile { Id = 42, Name = "EN" });

            Assert.Throws<TranslationProfileInUseException>(() => Subject.Delete(42));

            Mocker.GetMock<ITranslationProfileRepository>()
                  .Verify(r => r.Delete(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void delete_should_throw_when_profile_is_global_default()
        {
            Mocker.GetMock<IConfigService>()
                  .SetupGet(c => c.DefaultTranslationProfileId)
                  .Returns(99);

            Mocker.GetMock<ITranslationProfileRepository>()
                  .Setup(r => r.Get(99))
                  .Returns(new TranslationProfile { Id = 99, Name = "Default" });

            Assert.Throws<TranslationProfileInUseException>(() => Subject.Delete(99));

            Mocker.GetMock<ITranslationProfileRepository>()
                  .Verify(r => r.Delete(It.IsAny<int>()), Times.Never());
        }
    }
}
