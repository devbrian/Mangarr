using FluentValidation;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Download.Clients.Gateway
{
    /// <summary>
    /// Validator for <see cref="GatewayDownloadClientSettings"/>. Mirrors the SABnzbd connectivity
    /// floor (<c>origin/v5-develop:src/NzbDrone.Core/Download/Clients/Sabnzbd/SabnzbdSettings.cs</c>):
    /// <c>ValidHost</c> + port range + optional <c>ValidUrlBase</c> + a non-empty API key. The
    /// gateway requires an <c>X-Api-Key</c> header on every call (security V2; T-38-01-04), so the
    /// API key is mandatory (unlike SAB, which permits username/password as an alternative — the
    /// gateway has no such fallback).
    /// </summary>
    public class GatewayDownloadClientSettingsValidator : AbstractValidator<GatewayDownloadClientSettings>
    {
        public GatewayDownloadClientSettingsValidator()
        {
            RuleFor(c => c.Host).ValidHost();
            RuleFor(c => c.Port).InclusiveBetween(1, 65535);
            RuleFor(c => c.UrlBase).ValidUrlBase().When(c => c.UrlBase.IsNotNullOrWhiteSpace());
            RuleFor(c => c.ApiKey).NotEmpty();
        }
    }

    /// <summary>
    /// Settings POCO for the external manga-gateway download client (Phase 38, GWDL-01). Extends
    /// the Sonarr-canonical <see cref="DownloadClientSettingsBase{TSettings}"/> (free memberwise
    /// equality), mirroring SABnzbd's settings base.
    ///
    /// <para>
    /// The connectivity fields (Host/Port/UseSsl/UrlBase/ApiKey) are DUPLICATED verbatim from the
    /// Phase-37 <c>Indexers/Gateway/GatewaySettings.cs</c> shape — a shared
    /// <c>GatewaySettingsBase</c> is deliberately NOT extracted (Sonarr duplicates connectivity
    /// fields across every provider; RESEARCH Anti-Patterns). <c>outputFormat</c> is NOT a field
    /// here — it is the D-D hard-default <c>"cbz"</c> the client sets unconditionally.
    /// </para>
    /// </summary>
    public class GatewayDownloadClientSettings : DownloadClientSettingsBase<GatewayDownloadClientSettings>
    {
        private static readonly GatewayDownloadClientSettingsValidator Validator = new GatewayDownloadClientSettingsValidator();

        public GatewayDownloadClientSettings()
        {
            Host = "localhost";
            Port = 8080;
        }

        [FieldDefinition(0, Label = "Host", Type = FieldType.Textbox)]
        public string Host { get; set; }

        [FieldDefinition(1, Label = "Port", Type = FieldType.Textbox)]
        public int Port { get; set; }

        [FieldDefinition(2, Label = "UseSsl", Type = FieldType.Checkbox, HelpText = "DownloadClientSettingsUseSslHelpText")]
        [FieldToken(TokenField.HelpText, "UseSsl", "clientName", "Manga Gateway")]
        public bool UseSsl { get; set; }

        [FieldDefinition(3, Label = "UrlBase", Type = FieldType.Textbox, Advanced = true, HelpText = "DownloadClientSettingsUrlBaseHelpText")]
        [FieldToken(TokenField.HelpText, "UrlBase", "clientName", "Manga Gateway")]
        [FieldToken(TokenField.HelpText, "UrlBase", "url", "http://[host]:[port]/[urlBase]/api")]
        public string UrlBase { get; set; }

        // Security V2/V6 (T-38-01-01): PrivacyLevel.ApiKey masks the value in the UI + scrubs it
        // from logs. Sent as the X-Api-Key header on every gateway call; never written to _logger.
        [FieldDefinition(4, Label = "ApiKey", Type = FieldType.Textbox, Privacy = PrivacyLevel.ApiKey)]
        public string ApiKey { get; set; }

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
