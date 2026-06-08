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

        // BL-02 — resolve the source key the SourceKeySpecification CF spec matches on.
        //
        // quick-260608-gmm (Finding 3): PREFER the per-release ReleaseInfo.Source first. Post-Phase-39
        // a single Gateway indexer aggregates many upstreams (comix / mangaball / mangadex / mangadot),
        // so the indexer-level key (IHttpAggregatorSettings.SourceKey / ReleaseInfo.Indexer) is
        // IDENTICAL for every release and cannot differentiate a per-source Custom Format. GatewayParser
        // stamps the real upstream onto ReleaseInfo.Source (= r.SourceKey), and it survives
        // CleanupReleases — so when it is present it is the authoritative per-release source key and
        // wins over the aggregator key.
        //
        // Fallback (legacy / in-process paths that don't set Source): resolve the canonical Phase 3
        // D-17 key from the indexer instance. ReleaseInfo.Indexer carries the user-named instance
        // (e.g. "My MangaDex Mirror"); the spec needs the canonical key the indexer SettingsBase
        // exposes. Falls back to ReleaseInfo.Indexer (the user-named instance) when the indexer can't
        // be resolved (e.g. in tests, or if the indexer has been deleted between report fetch and
        // decision time) so naming-token consumers still get a non-null value.
        private string ResolveSourceKey(ReleaseInfo report)
        {
            if (report == null)
            {
                return null;
            }

            // quick-260608-gmm (Finding 3) — per-release gateway upstream wins when present.
            if (!string.IsNullOrWhiteSpace(report.Source))
            {
                return report.Source;
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
            // Sonarr divergence: Phase 15 Plan 15-10 — ReleaseDecisionInformation TV ctor stripped (SearchCriteriaBase DELETED).
            return GetDecisions(reports, new ReleaseDecisionInformation { PushedRelease = pushedRelease }).ToList();
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
                        // GH #118 — Sonarr-canonical contract restored: MangaParsingService.GetManga
                        // is the primary resolver on both RSS and search paths. The pre-77a114221
                        // `searchCriteria?.Manga ?? GetManga(...)` short-circuit was introduced to
                        // fix DEF-19-02-01 (MangaDex romanized attributes.title vs English
                        // Manga.CleanTitle silently failing every release as UnknownManga) but
                        // rendered the existing MangaSpecification Id-equality check structurally
                        // tautological — `subject.Manga.Id == searchCriteria.Manga.Id` is trivially
                        // true after the force-assign, so cross-title indexer noise was force-
                        // attributed to the searched manga and grabbable.
                        //
                        // The DEF-19-02-01 fix now lives one layer down: MangaParsingService.GetManga
                        // is multi-strategy (FindByTitle → FindByAlternativeTitle → FindByTitleInexact),
                        // Mangarr's mirror of Sonarr's ParsingService.GetSeries (preserved code at
                        // commit 2832eecb^). The alt-title strategy reads the JSON column populated
                        // by metadata sources from each provider's alt-title field set, which
                        // includes MangaDex's romanized attributes.title that SelectPreferredTitle
                        // discards. Search-path releases now resolve correctly, and the existing
                        // MangaSpecification (already wired) does its designed Id-cross-check job
                        // on the resulting RemoteChapter.Manga.
                        var searchCriteria = info?.MangaSearchCriteria;
                        var manga = _parsingService.GetManga(parsedChapterInfo.MangaTitle);

                        // GH #118 observability — surface cross-manga GetManga resolutions on
                        // the search path. When the parsed release title resolves to a different
                        // manga than the searched manga, that release is cross-title indexer
                        // noise: MangaSpecification will permanently reject it as
                        // MatchesAnotherSeries. The Warn log makes this audit-able from the log
                        // file during the first release cycle so we can spot regressions or
                        // alt-title-coverage gaps before complaints land. Logged BEFORE Map() so
                        // every cross-manga case is captured, even for releases whose Chapters
                        // resolution would later fail.
                        if (searchCriteria?.Manga != null
                            && manga != null
                            && manga.Id != searchCriteria.Manga.Id)
                        {
                            _logger.Warn(
                                "Search-path release '{0}' resolved to manga '{1}' (id={2}) but search target is manga '{3}' (id={4}); MangaSpecification will reject as wrong manga.",
                                report.Title,
                                manga.Title,
                                manga.Id,
                                searchCriteria.Manga.Title,
                                searchCriteria.Manga.Id);
                        }

                        var remoteChapter = _parsingService.Map(parsedChapterInfo, manga, searchCriteria?.Chapters);

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
                                SourceKey = ResolveSourceKey(report),    // BL-02 — per-release ReleaseInfo.Source (gateway upstream) preferred; indexer key fallback (quick-260608-gmm Finding 3)
                                Size = report.Size,
                                IndexerFlags = report.IndexerFlags,
                                Filename = report.Title
                            };
                            remoteChapter.CustomFormats = _formatCalculator.ParseCustomFormat(cfInput);

                            // Score via the manga's CustomFormatProfile (per-Manga FK ?? global default).
                            // Mirrors DownloadDecisionMaker.cs:119 — TV reads remoteEpisode.Series.QualityProfile;
                            // manga reads Manga.CustomFormatProfileId ?? Config.DefaultCustomFormatProfileId.
                            // WR-08: resolve the id ONCE here and stamp it on RemoteChapter so the spec
                            // reads the SAME id the maker used to compute the score. Avoids the hazard
                            // where an admin flips Config.DefaultCustomFormatProfileId between the
                            // maker's read and the spec's read, leaving score/gate keyed off different
                            // profiles.
                            var cfProfileId = remoteChapter.Manga?.CustomFormatProfileId ?? _configService.DefaultCustomFormatProfileId;
                            remoteChapter.ResolvedCustomFormatProfileId = cfProfileId;

                            // BL-03 (DEF-19-02-01): degrade gracefully when the resolved id is
                            // not a real profile FK. A Manga can carry CustomFormatProfileId == 0
                            // (the int default — the AddManga modal sends 0 when no CF profile is
                            // picked and none is the global default), and the id could also point
                            // at a since-deleted profile. CustomFormatProfileService.Get throws
                            // ModelNotFoundException on a missing row — left unguarded that
                            // exception is caught by the outer try/catch and turns EVERY release
                            // into a DecisionError rejection, so the InteractiveSearch modal
                            // renders a results table the React row code then crashes on. A
                            // missing/zero profile means "no CF scoring" — score 0, same as the
                            // no-profile branch. Mirrors the Sonarr-canonical posture where a
                            // release with no usable profile is still a renderable decision.
                            if (cfProfileId.HasValue && cfProfileId.Value > 0)
                            {
                                try
                                {
                                    var cfProfile = _customFormatProfileService.Get(cfProfileId.Value);
                                    remoteChapter.CustomFormatScore = cfProfile?.CalculateCustomFormatScore(remoteChapter.CustomFormats) ?? 0;
                                }
                                catch (Datastore.ModelNotFoundException)
                                {
                                    _logger.Warn("CustomFormatProfile {0} not found (orphaned FK); scoring release 0", cfProfileId.Value);
                                    remoteChapter.CustomFormatScore = 0;
                                }
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
