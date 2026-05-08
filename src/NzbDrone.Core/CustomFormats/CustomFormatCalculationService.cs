using System.Collections.Generic;
using System.Linq;
using NLog;

namespace NzbDrone.Core.CustomFormats
{
    // Sonarr divergence: Phase 15 W-3 (CONTRACTS-AUDIT Wave A Cluster 3) - 6 TV-shape
    // ParseCustomFormat overloads removed:
    //   ParseCustomFormat(RemoteEpisode, long size)
    //   ParseCustomFormat(EpisodeFile, Series)
    //   ParseCustomFormat(EpisodeFile)
    //   ParseCustomFormat(Blocklist, Series)
    //   ParseCustomFormat(EpisodeHistory, Series)
    //   ParseCustomFormat(LocalEpisode, string)
    // Manga overload preserved: ParseCustomFormat(MangaCustomFormatInput input).
    // ~50-caller cascade rebound or deleted in same atomic commit per CONTRACTS-AUDIT
    // plan-author rule + Phase 15 W-1/W-2/W-3 cluster atomicity.
    public interface ICustomFormatCalculationService
    {
        // Phase 5 D-09 - manga-side overload. MangaCustomFormatInput inherits from
        // CustomFormatInput so the existing private ParseCustomFormat(CustomFormatInput)
        // path runs the same Specifications.IsSatisfiedBy(input) loop. Manga CF specs
        // (Plan 05-05 deliverable) downcast `input is MangaCustomFormatInput` per Pitfall 4.
        List<CustomFormat> ParseCustomFormat(MangaCustomFormatInput input);
    }

    public class CustomFormatCalculationService : ICustomFormatCalculationService
    {
        private readonly ICustomFormatService _formatService;
        private readonly Logger _logger;

        public CustomFormatCalculationService(ICustomFormatService formatService, Logger logger)
        {
            _formatService = formatService;
            _logger = logger;
        }

        // Phase 5 D-09 - manga-side overload. Delegates to the private CustomFormatInput
        // path; manga CF specs receive the input as MangaCustomFormatInput (subclass) and
        // downcast per Pitfall 4 mitigation.
        public List<CustomFormat> ParseCustomFormat(MangaCustomFormatInput input)
        {
            return ParseCustomFormat((CustomFormatInput)input, _formatService.All());
        }

        private static List<CustomFormat> ParseCustomFormat(CustomFormatInput input, List<CustomFormat> allCustomFormats)
        {
            var matches = new List<CustomFormat>();

            foreach (var customFormat in allCustomFormats)
            {
                var specificationMatches = customFormat.Specifications
                    .GroupBy(t => t.GetType())
                    .Select(g => new SpecificationMatchesGroup
                    {
                        Matches = g.ToDictionary(t => t, t => t.IsSatisfiedBy(input))
                    })
                    .ToList();

                if (specificationMatches.All(x => x.DidMatch))
                {
                    matches.Add(customFormat);
                }
            }

            return matches.OrderBy(x => x.Name).ToList();
        }
    }
}
