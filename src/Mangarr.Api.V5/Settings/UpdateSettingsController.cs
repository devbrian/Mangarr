using FluentValidation;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Update;
using NzbDrone.Core.Validation.Paths;
using Mangarr.Http;

namespace Mangarr.Api.V5.Settings;

[V5ApiController("settings/update")]
public class UpdateSettingsController : SettingsController<UpdateSettingsResource>
{
    public UpdateSettingsController(IConfigFileProvider configFileProvider, IConfigService configService)
        : base(configFileProvider, configService)
    {
        SharedValidator.RuleFor(c => c.UpdateScriptPath)
            .IsValidPath()
            .When(c => c.UpdateMechanism == UpdateMechanism.Script);
    }

    protected override UpdateSettingsResource ToResource(IConfigFileProvider configFile, IConfigService model)
    {
        return UpdateSettingsResourceMapper.ToResource(configFile);
    }
}
