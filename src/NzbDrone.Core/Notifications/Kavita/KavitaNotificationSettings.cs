using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Notifications.Kavita
{
    // Sonarr divergence: NEW manga-reader notification per Phase 6 D-16 + D-17 — see DIVERGENCE.md.
    // D-16 (vs Komga's Pitfall 2): Kavita HAS a scan-all endpoint (/api/Library/scan-all), so
    // LibraryId is OPTIONAL — null means scan-all, positive int means scan that one library.
    public class KavitaNotificationSettingsValidator : AbstractValidator<KavitaNotificationSettings>
    {
        public KavitaNotificationSettingsValidator()
        {
            RuleFor(c => c.Url).ValidRootUrl();
            RuleFor(c => c.ApiKey).NotEmpty();

            // D-16: LibraryId OPTIONAL (Kavita has scan-all). Reject only if explicitly set
            // to 0 or negative — null is the valid scan-all signal.
            When(c => c.LibraryId.HasValue, () =>
            {
                RuleFor(c => c.LibraryId.Value)
                    .GreaterThan(0)
                    .WithName("LibraryId")
                    .WithMessage("LibraryId must be positive when set; leave blank for scan-all");
            });
        }
    }

    public class KavitaNotificationSettings : NotificationSettingsBase<KavitaNotificationSettings>
    {
        private static readonly KavitaNotificationSettingsValidator Validator = new();

        [FieldDefinition(0, Label = "KavitaNotificationUrl", HelpText = "KavitaNotificationUrlHelpText")]
        public string Url { get; set; }

        [FieldDefinition(1, Label = "KavitaNotificationApiKey", Privacy = PrivacyLevel.ApiKey, HelpText = "KavitaNotificationApiKeyHelpText")]
        public string ApiKey { get; set; }

        [FieldDefinition(2, Label = "KavitaNotificationLibraryId", Type = FieldType.Number, HelpText = "KavitaNotificationLibraryIdHelpText")]
        public int? LibraryId { get; set; }

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
