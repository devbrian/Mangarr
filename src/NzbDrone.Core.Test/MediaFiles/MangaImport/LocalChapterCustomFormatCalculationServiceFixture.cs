using System;
using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles.MangaImport;
using NzbDrone.Core.Profiles.CustomFormats;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.MangaImport
{
    // Regression guard for debug session queue-item-stuck-importing (2026-06-04).
    //
    // A Manga carrying CustomFormatProfileId == 0 (the int-default "no profile picked"
    // sentinel the AddManga modal sends) made ResolveCustomFormatProfile call
    // CustomFormatProfileService.Get(0), which throws ModelNotFoundException because
    // profile rows start at id 1. That exception propagated out of
    // MangaImportDecisionMaker.GetDecision and was swallowed by
    // MangaDownloadProcessingService.Process, wedging the tracked download in
    // "Downloaded - Importing" on an infinite, silent retry. This mirrors the BL-03 /
    // DEF-19-02-01 hardening already present on the download-decision side
    // (MangaDownloadDecisionMaker.cs:238-254). A missing/zero/orphaned profile must
    // degrade to "no CF scoring" (score 0), never throw.
    [TestFixture]
    public class LocalChapterCustomFormatCalculationServiceFixture
        : CoreTest<LocalChapterCustomFormatCalculationService>
    {
        private LocalChapter _localChapter;

        [SetUp]
        public void Setup()
        {
            var manga = Builder<NzbDrone.Core.Manga.Manga>.CreateNew()
                .With(m => m.Id = 2)
                .Build();

            _localChapter = new LocalChapter
            {
                Manga = manga,
                Path = @"C:\Staging\The Forgotten Field - Chapter 5 (en) [Speedcat].cbz",
                Size = 100
            };

            // CF parsing itself is exercised elsewhere — return an empty match set so the
            // only variable under test is profile resolution.
            Mocker.GetMock<ICustomFormatCalculationService>()
                  .Setup(c => c.ParseCustomFormat(It.IsAny<MangaCustomFormatInput>()))
                  .Returns(new List<CustomFormat>());
        }

        [Test]
        public void should_not_throw_and_score_zero_when_custom_format_profile_id_is_zero()
        {
            _localChapter.Manga.CustomFormatProfileId = 0;

            Subject.UpdateChapterCustomFormats(_localChapter);

            _localChapter.CustomFormatScore.Should().Be(0);

            // The 0 sentinel must never reach the repository.
            Mocker.GetMock<ICustomFormatProfileService>()
                  .Verify(s => s.Get(It.IsAny<int>()), Times.Never);
        }

        [Test]
        public void should_not_throw_and_score_zero_when_profile_fk_is_null_and_default_is_zero()
        {
            _localChapter.Manga.CustomFormatProfileId = null;
            Mocker.GetMock<IConfigService>()
                  .SetupGet(c => c.DefaultCustomFormatProfileId)
                  .Returns(0);

            Subject.UpdateChapterCustomFormats(_localChapter);

            _localChapter.CustomFormatScore.Should().Be(0);
            Mocker.GetMock<ICustomFormatProfileService>()
                  .Verify(s => s.Get(It.IsAny<int>()), Times.Never);
        }

        [Test]
        public void should_not_throw_and_score_zero_when_profile_fk_is_orphaned()
        {
            _localChapter.Manga.CustomFormatProfileId = 999;
            Mocker.GetMock<ICustomFormatProfileService>()
                  .Setup(s => s.Get(999))
                  .Throws(new ModelNotFoundException(typeof(CustomFormatProfile), 999));

            Subject.UpdateChapterCustomFormats(_localChapter);

            _localChapter.CustomFormatScore.Should().Be(0);
            Mocker.GetMock<ICustomFormatProfileService>()
                  .Verify(s => s.Get(999), Times.Once);

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_resolve_profile_when_id_is_a_valid_fk()
        {
            _localChapter.Manga.CustomFormatProfileId = 1;
            Mocker.GetMock<ICustomFormatProfileService>()
                  .Setup(s => s.Get(1))
                  .Returns(new CustomFormatProfile { Id = 1 });

            Action act = () => Subject.UpdateChapterCustomFormats(_localChapter);

            act.Should().NotThrow();
            _localChapter.CustomFormatScore.Should().Be(0);
            Mocker.GetMock<ICustomFormatProfileService>()
                  .Verify(s => s.Get(1), Times.Once);
        }
    }
}
