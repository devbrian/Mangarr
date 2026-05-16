using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Notifications.Komga
{
    // Sonarr divergence: NEW manga-reader notification per Phase 6 D-15 + D-17 — see DIVERGENCE.md.
    // Pitfall 2 (HIGH-priority): LibraryId is REQUIRED. Komga has NO scan-all endpoint;
    // only POST /api/v1/libraries/{id}/scan exists per komga.org/docs/openapi/library-scan/.
    public class KomgaNotificationSettingsValidator : AbstractValidator<KomgaNotificationSettings>
    {
        public KomgaNotificationSettingsValidator()
        {
            RuleFor(c => c.Url).ValidRootUrl();
            RuleFor(c => c.ApiKey).NotEmpty();

            // Pitfall 2 mitigation — LibraryId required (Komga has no scan-all).
            RuleFor(c => c.LibraryId)
                .NotNull()
                .GreaterThan(0)
                .WithMessage("Library is required (Komga has no scan-all endpoint; pick a library)");
        }
    }

    public class KomgaNotificationSettings : NotificationSettingsBase<KomgaNotificationSettings>
    {
        private static readonly KomgaNotificationSettingsValidator Validator = new();

        [FieldDefinition(0, Label = "KomgaNotificationUrl", HelpText = "KomgaNotificationUrlHelpText")]
        public string Url { get; set; }

        [FieldDefinition(1, Label = "KomgaNotificationApiKey", Privacy = PrivacyLevel.ApiKey, HelpText = "KomgaNotificationApiKeyHelpText")]
        public string ApiKey { get; set; }

        [FieldDefinition(2, Label = "KomgaNotificationLibraryId", Type = FieldType.Number, HelpText = "KomgaNotificationLibraryIdHelpText")]
        public int? LibraryId { get; set; }

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
