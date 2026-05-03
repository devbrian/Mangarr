using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Download.Clients.InProcess
{
    public class InProcessImageDownloadClientSettingsValidator : AbstractValidator<InProcessImageDownloadClientSettings>
    {
        public InProcessImageDownloadClientSettingsValidator()
        {
            RuleFor(c => c.DownloadsPerSource).GreaterThanOrEqualTo(1).LessThanOrEqualTo(8);
            RuleFor(c => c.PagesPerChapter).GreaterThanOrEqualTo(1).LessThanOrEqualTo(16);
            RuleFor(c => c.RetentionDays).GreaterThanOrEqualTo(0);
        }
    }

    public class InProcessImageDownloadClientSettings : DownloadClientSettingsBase<InProcessImageDownloadClientSettings>
    {
        private static readonly InProcessImageDownloadClientSettingsValidator Validator = new();

        [FieldDefinition(0, Label = "InProcessDownloadsPerSource", Type = FieldType.Number, HelpText = "Concurrent chapter downloads per source (RateLimitKey bucket)")]
        public int DownloadsPerSource { get; set; } = 2;

        [FieldDefinition(1, Label = "InProcessPagesPerChapter", Type = FieldType.Number, HelpText = "Concurrent page downloads within a single chapter")]
        public int PagesPerChapter { get; set; } = 4;

        [FieldDefinition(2, Label = "InProcessRetentionDays", Type = FieldType.Number, HelpText = "Days to keep failed-chapter rows for manual retry from History")]
        public int RetentionDays { get; set; } = 7;

        public override NzbDroneValidationResult Validate() => new(Validator.Validate(this));
    }
}
