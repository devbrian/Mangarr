using FluentAssertions;
using FluentValidation.Results;
using NUnit.Framework;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Test.NotificationTests
{
    // Phase 8 Plan 99-08 — verifies the Pitfall 7 reflection-backed Supports* contract:
    // a NotificationBase subclass that does NOT override OnMangaAdd/Delete/Rename returns
    // false for the corresponding SupportsOn* properties. v1.1+ providers that DO override
    // will return true automatically.
    //
    // Post-Phase-15 backfill — same contract extended to cover OnChapterFileDelete and
    // OnChapterFileDeleteForUpgrade hooks (manga siblings of TV-deleted EpisodeFileDelete
    // pair). Surface-only addition; no v1 publisher fans these out.
    [TestFixture]
    public class NotificationBaseMangaSupportsFixture
    {
        [Test]
        public void NonOverriding_subclass_returns_false_for_SupportsOnMangaAdd()
        {
            new TestNotificationStub().SupportsOnMangaAdd.Should().BeFalse();
        }

        [Test]
        public void NonOverriding_subclass_returns_false_for_SupportsOnMangaDelete()
        {
            new TestNotificationStub().SupportsOnMangaDelete.Should().BeFalse();
        }

        [Test]
        public void NonOverriding_subclass_returns_false_for_SupportsOnMangaRename()
        {
            new TestNotificationStub().SupportsOnMangaRename.Should().BeFalse();
        }

        [Test]
        public void NonOverriding_subclass_returns_false_for_SupportsOnChapterFileDelete()
        {
            new TestNotificationStub().SupportsOnChapterFileDelete.Should().BeFalse();
        }

        [Test]
        public void NonOverriding_subclass_returns_false_for_SupportsOnChapterFileDeleteForUpgrade()
        {
            new TestNotificationStub().SupportsOnChapterFileDeleteForUpgrade.Should().BeFalse();
        }

        private class TestNotificationStub : NotificationBase<TestNotificationSettings>
        {
            public override string Name => "TestStub";
            public override string Link => "https://example.com";
            public override ValidationResult Test() => new ValidationResult();
        }

        private class TestNotificationSettings : NotificationSettingsBase<TestNotificationSettings>
        {
            public override NzbDroneValidationResult Validate()
            {
                return new NzbDroneValidationResult();
            }
        }
    }
}
