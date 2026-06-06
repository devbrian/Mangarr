using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Localization
{
    [TestFixture]
    public class LocalizationServiceFixture : CoreTest<LocalizationService>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IConfigService>().Setup(m => m.UILanguage).Returns((int)Language.English);

            Mocker.GetMock<IAppFolderInfo>().Setup(m => m.StartUpFolder).Returns(TestContext.CurrentContext.TestDirectory);
        }

        [Test]
        public void should_get_string_in_dictionary_if_lang_exists_and_string_exists()
        {
            var localizedString = Subject.GetLocalizedString("UiLanguage");

            localizedString.Should().Be("UI Language");
        }

        [Test]
        public void should_get_string_in_french()
        {
            Mocker.GetMock<IConfigService>().Setup(m => m.UILanguage).Returns((int)Language.French);

            var localizedString = Subject.GetLocalizedString("UiLanguage");

            localizedString.Should().Be("Langue de l'interface utilisateur");

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_get_string_in_default_dictionary_if_unknown_language_and_string_exists()
        {
            Mocker.GetMock<IConfigService>().Setup(m => m.UILanguage).Returns(0);
            var localizedString = Subject.GetLocalizedString("UiLanguage");

            localizedString.Should().Be("UI Language");
        }

        [Test]
        public void should_return_argument_if_string_doesnt_exists()
        {
            var localizedString = Subject.GetLocalizedString("badString");

            localizedString.Should().Be("badString");
        }

        [Test]
        public void should_return_argument_if_string_doesnt_exists_default_lang()
        {
            var localizedString = Subject.GetLocalizedString("badString");

            localizedString.Should().Be("badString");
        }

        [Test]
        public void should_resolve_indexer_source_unavailable_health_check_message_and_interpolate_source_keys()
        {
            // Root cause #1: the key MUST exist in en.json — a missing key makes
            // GetLocalizedString return the raw key string verbatim (proven by
            // should_return_argument_if_string_doesnt_exists), which is what the
            // System → Health page was literally rendering.
            // Root cause #2: the {sourceKeys} token MUST interpolate the supplied
            // source names so the warning names which source(s) are unavailable.
            var localizedString = Subject.GetLocalizedString(
                "IndexerSourceUnavailableHealthCheckMessage",
                new Dictionary<string, object> { { "sourceKeys", "mangadex, comix.to" } });

            // Guards root cause #1: a resolved value is NOT the raw key string.
            localizedString.Should().NotBe("IndexerSourceUnavailableHealthCheckMessage");

            // Guards root cause #2: the {sourceKeys} token interpolated the source names.
            localizedString.Should().Contain("mangadex");
            localizedString.Should().Contain("comix.to");
        }

        [Test]
        public void should_throw_if_empty_string_passed()
        {
            Assert.Throws<ArgumentNullException>(() => Subject.GetLocalizedString(""));
        }

        [Test]
        public void should_throw_if_null_string_passed()
        {
            Assert.Throws<ArgumentNullException>(() => Subject.GetLocalizedString(null));
        }
    }
}
