using System.Reflection;
using Mangarr.Http;
using Mangarr.Http.REST;
using Mangarr.Http.REST.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;

namespace Mangarr.Api.V5.Config;

[V5ApiController("config/downloadclient")]
public class DownloadClientConfigController : RestController<DownloadClientConfigResource>
{
    private readonly IConfigService _configService;

    public DownloadClientConfigController(IConfigService configService)
    {
        _configService = configService;
    }

    protected override DownloadClientConfigResource GetResourceById(int id)
    {
        return GetConfig();
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<DownloadClientConfigResource> GetDownloadClientConfig()
    {
        return TypedResults.Ok(GetConfig());
    }

    [RestPutById]
    [Consumes("application/json")]
    [Produces("application/json")]
    public Results<Accepted<DownloadClientConfigResource>, NotFound> SaveConfig([FromBody] DownloadClientConfigResource resource)
    {
        var dictionary = resource.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .ToDictionary(prop => prop.Name, prop => prop.GetValue(resource, null));

        _configService.SaveConfigDictionary(dictionary);

        return TypedAccepted(resource.Id);
    }

    private DownloadClientConfigResource GetConfig()
    {
        var resource = DownloadClientConfigResourceMapper.ToResource(_configService);
        resource.Id = 1;
        return resource;
    }
}
