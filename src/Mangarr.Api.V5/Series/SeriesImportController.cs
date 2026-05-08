using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Tv;
using Mangarr.Http;

namespace Mangarr.Api.V5.Series
{
    [V5ApiController("series/import")]
    public class SeriesImportController : Controller
    {
        private readonly IAddSeriesService _addSeriesService;

        public SeriesImportController(IAddSeriesService addSeriesService)
        {
            _addSeriesService = addSeriesService;
        }

        [HttpPost]
        public Ok<List<SeriesResource>> Import([FromBody] List<SeriesResource> resource)
        {
            var newSeries = resource.ToModel();

            return TypedResults.Ok(_addSeriesService.AddSeries(newSeries).ToResource());
        }
    }
}
