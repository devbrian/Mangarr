using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.NotificationTests
{
    // Phase 6 Plan 14 — BL-01 coverage. SetProviderCharacteristics on a brand-new
    // provider (Id == 0) must scope Definition.OnChapterImport to the provider's
    // actual capability — TV providers (SupportsOnChapterImport == false) do NOT
    // silently land with OnChapterImport = true.
    [TestFixture]
    public class NotificationFactoryFixture : CoreTest<NotificationFactory>
    {
        private static NotificationDefinition NewDefinition()
        {
            // Default state from NotificationDefinition.cs:29 is OnChapterImport = true.
            return new NotificationDefinition { Id = 0, OnChapterImport = true };
        }

        [Test]
        public void SetProviderCharacteristics_clears_OnChapterImport_for_TV_provider_on_first_save()
        {
            var provider = new Mock<INotification>();
            provider.SetupGet(p => p.SupportsOnChapterImport).Returns(false);

            // Other Supports* getters default to false via Moq — fine for this test.
            var def = NewDefinition();
            Subject.SetProviderCharacteristics(provider.Object, def);

            def.OnChapterImport.Should().BeFalse(
                "BL-01: TV providers (SupportsOnChapterImport == false) must NOT have OnChapterImport silently enabled on first save.");
            def.SupportsOnChapterImport.Should().BeFalse();
        }

        [Test]
        public void SetProviderCharacteristics_keeps_OnChapterImport_true_for_manga_provider_on_first_save()
        {
            var provider = new Mock<INotification>();
            provider.SetupGet(p => p.SupportsOnChapterImport).Returns(true);

            var def = NewDefinition();
            Subject.SetProviderCharacteristics(provider.Object, def);

            def.OnChapterImport.Should().BeTrue(
                "Komga/Kavita providers (SupportsOnChapterImport == true) keep the default OnChapterImport = true on first save.");
            def.SupportsOnChapterImport.Should().BeTrue();
        }

        [Test]
        public void SetProviderCharacteristics_does_not_overwrite_existing_OnChapterImport_choice_on_resave()
        {
            // Id != 0 means the user has saved this provider before; their explicit
            // OnChapterImport choice (true OR false) must survive subsequent re-saves.
            var provider = new Mock<INotification>();
            provider.SetupGet(p => p.SupportsOnChapterImport).Returns(true);

            var def = new NotificationDefinition { Id = 42, OnChapterImport = false };
            Subject.SetProviderCharacteristics(provider.Object, def);

            def.OnChapterImport.Should().BeFalse(
                "Existing user toggle (Id != 0) must not be overwritten by SetProviderCharacteristics.");
        }
    }
}
