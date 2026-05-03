using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Http;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.CustomFormats;

namespace NzbDrone.Core.DecisionEngine.Manga
{
    // Sonarr divergence: NEW manga decision orchestrator per Phase 5 D-05 — see DIVERGENCE.md.
    // Mirrors src/NzbDrone.Core/DecisionEngine/DownloadDecisionMaker.cs verbatim with type swap.
    //
    // CRITICAL Pitfall 5 mitigation: this orchestrator MUST call _formatCalculator.ParseCustomFormat
    // (CF augmentation site) AND iterate _specifications (priority-grouped exec) — Wave 4's
    // MangaDownloadDecisionMakerEndToEndFixture asserts both call sites fire end-to-end. The
    // service-injected-but-not-called regression class (F-01) is mitigated by physically
    // invoking both at the call sites below.
    //
    // Parsing pipeline differs from TV: manga uses a two-step
    //     MangaParser.ParseChapterTitle(title) -> ParsedChapterInfo
    //     IMangaParsingService.GetManga(title) -> Manga
    //     IMangaParsingService.Map(parsed, manga, existingChapters) -> RemoteChapter
    // because the Phase 2 IMangaParsingService surface is intentionally narrow (D-03).
    public class MangaDownloadDecisionMaker : IMakeMangaDownloadDecision
    {
        private readonly IEnumerable<IMangaDecisionEngineSpecification> _specifications;
        private readonly IMangaParsingService _parsingService;
        private readonly ICustomFormatCalculationService _formatCalculator;
        private readonly ICustomFormatProfileService _customFormatProfileService;
        private readonly IIndexerFactory _indexerFactory;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public MangaDownloadDecisionMaker(
            IEnumerable<IMangaDecisionEngineSpecification> specifications,    // DryIoc auto-discovery (pattern S1)
            IMangaParsingService parsingService,
            ICustomFormatCalculationService formatCalculator,
            ICustomFormatProfileService customFormatProfileService,
            IIndexerFactory indexerFactory,
            IConfigService configService,
            Logger logger)
        {
            _specifications = specifications;
            _parsingService = parsingService;
            _formatCalculator = formatCalculator;
            _customFormatProfileService = customFormatProfileService;
            _indexerFactory = indexerFactory;
            _configService = configService;
            _logger = logger;
        }

        // BL-02 — resolve the canonical Phase 3 D-17 source key (e.g. "mangadex" / "comix.to")
        // from the indexer instance. ReleaseInfo.Indexer carries the user-named instance
        // (e.g. "My MangaDex Mirror"); the SourceKeySpecification CF spec needs the canonical
        // key the indexer SettingsBase exposes. Falls back to ReleaseInfo.Indexer (the user-
        // named instance) when the indexer can't be resolved (e.g. in tests, or if the indexer
        // has been deleted between report fetch and decision time) so naming-token consumers
        // still get a non-null value.
        private string ResolveSourceKey(ReleaseInfo report)
        {
            if (report == null)
            {
                return null;
            }

            try
            {
                if (report.IndexerId > 0)
                {
                    var def = _indexerFactory?.Get(report.IndexerId);
                    if (def?.Settings is IHttpAggregatorSettings aggregator
                        && !string.IsNullOrWhiteSpace(aggregator.SourceKey))
                    {
                        return aggregator.SourceKey;
                    }
                }
            }
            catch (System.Collections.Generic.KeyNotFoundException)
            {
                // Indexer was deleted between report fetch and decision time. Fall through.
            }
            catch (Exception ex)
            {
                _logger.Trace(ex, "Failed to resolve canonical SourceKey for indexer id {0}; falling back to instance name", report.IndexerId);
            }

            return report.Indexer;
        }

        public List<MangaDownloadDecision> GetRssDecision(List<ReleaseInfo> reports, bool pushedRelease = false)
        {
            return GetDecisions(reports, new ReleaseDecisionInformation(pushedRelease, (SearchCriteriaBase)null)).ToList();
        }

        public List<MangaDownloadDecision> GetSearchDecision(List<ReleaseInfo> reports, MangaSearchCriteriaBase searchCriteria)
        {
            return GetDecisions(reports, ReleaseDecisionInformation.FromMangaSearch(false, searchCriteria)).ToList();
        }

        // Mirrors DownloadDecisionMaker.GetDecisions enumeration shape (lines 58-178 area).
        private IEnumerable<MangaDownloadDecision> GetDecisions(List<ReleaseInfo> reports, ReleaseDecisionInformation info)
        {
            if (reports.Any())
            {
                _logger.ProgressInfo("Processing {0} manga releases", reports.Count);
            }
            else
            {
                _logger.ProgressInfo("No manga results found");
            }

            var reportNumber = 1;

            foreach (var report in reports)
            {
                MangaDownloadDecision decision = null;
                _logger.ProgressTrace("Processing manga release {0}/{1}", reportNumber, reports.Count);

                try
                {
                    var parsedChapterInfo = MangaParser.ParseChapterTitle(report.Title);

                    if (parsedChapterInfo != null && !parsedChapterInfo.MangaTitle.IsNullOrWhiteSpace())
                    {
                        var manga = _parsingService.GetManga(parsedChapterInfo.MangaTitle);
                        var remoteChapter = _parsingService.Map(parsedChapterInfo, manga, null);

                        if (remoteChapter == null)
                        {
                            // Manga not found in DB — caller's "unknown manga" semantics live in Phase 6.
                            // Surface a temporary-rejection so the release can be retried after the user
                            // adds the manga. (Mirrors TV UnknownSeries semantics; we reuse the existing
                            // enum value rather than introducing a new UnknownManga value in Phase 5.)
                            remoteChapter = new RemoteChapter
                            {
                                Release = report,
                                ParsedChapterInfo = parsedChapterInfo
                            };
                            decision = new MangaDownloadDecision(
                                remoteChapter,
                                new DownloadRejection(DownloadRejectionReason.UnknownSeries, "Unknown Manga"));
                        }
                        else
                        {
                            remoteChapter.Release = report;

                            // CF augmentation per Pitfall 5 — service MUST be called, not just injected.
                            // Mirrors DownloadDecisionMaker.cs:117-121.
                            var cfInput = new MangaCustomFormatInput
                            {
                                ChapterInfo = remoteChapter.ParsedChapterInfo,
                                Manga = remoteChapter.Manga,
                                Release = report,
                                SourceKey = ResolveSourceKey(report),    // BL-02 — canonical D-17 key (mangadex / comix.to)
                                Size = report.Size,
                                IndexerFlags = report.IndexerFlags,
                                Filename = report.Title
                            };
                            remoteChapter.CustomFormats = _formatCalculator.ParseCustomFormat(cfInput);

                            // Score via the manga's CustomFormatProfile (per-Manga FK ?? global default).
                            // Mirrors DownloadDecisionMaker.cs:119 — TV reads remoteEpisode.Series.QualityProfile;
                            // manga reads Manga.CustomFormatProfileId ?? Config.DefaultCustomFormatProfileId.
                            var cfProfileId = remoteChapter.Manga?.CustomFormatProfileId ?? _configService.DefaultCustomFormatProfileId;
                            if (cfProfileId.HasValue)
                            {
                                var cfProfile = _customFormatProfileService.Get(cfProfileId.Value);
                                remoteChapter.CustomFormatScore = cfProfile?.CalculateCustomFormatScore(remoteChapter.CustomFormats) ?? 0;
                            }
                            else
                            {
                                remoteChapter.CustomFormatScore = 0;
                            }

                            _logger.Trace(
                                "Custom Format Score of '{0}' [{1}] calculated for '{2}'",
                                remoteChapter.CustomFormatScore,
                                remoteChapter.CustomFormats?.ConcatToString(),
                                report.Title);

                            decision = GetDecisionForReport(remoteChapter, info);
                        }
                    }
                    else
                    {
                        // Unparseable release — surface as UnableToParse rejection so callers can log it.
                        var remoteChapter = new RemoteChapter { Release = report, ParsedChapterInfo = parsedChapterInfo };
                        decision = new MangaDownloadDecision(
                            remoteChapter,
                            new DownloadRejection(DownloadRejectionReason.UnableToParse, "Unable to parse manga release"));
                    }
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Couldn't process manga release '{0}'", report.Title);

                    var remoteChapter = new RemoteChapter { Release = report };
                    decision = new MangaDownloadDecision(
                        remoteChapter,
                        new DownloadRejection(DownloadRejectionReason.Error, "Unexpected error processing manga release"));
                }

                reportNumber++;

                if (decision != null)
                {
                    yield return decision;
                }
            }
        }

        // Mirrors DownloadDecisionMaker.GetDecisionForReport (lines 180-197) verbatim.
        private MangaDownloadDecision GetDecisionForReport(RemoteChapter remoteChapter, ReleaseDecisionInformation information)
        {
            var reasons = Array.Empty<DownloadRejection>();

            foreach (var specifications in _specifications.GroupBy(v => v.Priority).OrderBy(v => v.Key))
            {
                reasons = specifications.Select(c => EvaluateSpec(c, remoteChapter, information))
                                        .Where(c => c != null)
                                        .ToArray();

                if (reasons.Any())
                {
                    break;
                }
            }

            return new MangaDownloadDecision(remoteChapter, reasons.ToArray());
        }

        // Mirrors DownloadDecisionMaker.EvaluateSpec (lines 199-219) verbatim with type swap.
        private DownloadRejection EvaluateSpec(IMangaDecisionEngineSpecification spec, RemoteChapter remoteChapter, ReleaseDecisionInformation information)
        {
            try
            {
                var result = spec.IsSatisfiedBy(remoteChapter, information);

                if (!result.Accepted)
                {
                    return new DownloadRejection(result.Reason, result.Message, spec.Type);
                }
            }
            catch (Exception e)
            {
                e.Data.Add("report", remoteChapter.Release?.ToJson());
                e.Data.Add("parsed", remoteChapter.ParsedChapterInfo?.ToJson());
                _logger.Error(e, "Couldn't evaluate manga decision on {0}", remoteChapter.Release?.Title);
                return new DownloadRejection(DownloadRejectionReason.DecisionError, $"{spec.GetType().Name}: {e.Message}");
            }

            return null;
        }
    }
}
