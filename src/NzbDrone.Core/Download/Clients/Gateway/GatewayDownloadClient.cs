using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download.Clients.Gateway.Responses;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.RemotePathMappings;

namespace NzbDrone.Core.Download.Clients.Gateway
{
    /// <summary>
    /// Phase 38 GWDL-01..04 — the "Mangarr Gateway" out-of-process <see cref="IDownloadClient"/>.
    /// The client half of the SABnzbd three-file decomposition (state-mapping + <c>GetStatus</c> +
    /// <c>Test</c>); all HTTP I/O lives in <see cref="IGatewayDownloadProxy"/>.
    ///
    /// <para>
    /// Sonarr divergence: extends <see cref="DownloadClientBase{TSettings}"/> DIRECTLY (NOT
    /// <c>UsenetClientBase</c> as SABnzbd does) and takes the manga-shape five-service ctor
    /// (copied from <c>InProcessImageDownloadClient</c>, NOT SAB's <c>UsenetClientBase(httpClient, …)</c>) —
    /// the client gets NO HTTP-client dependency (all transport belongs to the proxy). Manga is
    /// neither Usenet nor Torrent: <see cref="DownloadProtocol.Http"/> (Phase 1 D-04).
    /// </para>
    ///
    /// <para>
    /// Disabled-by-default: auto-discovered by the ThingiProvider reflection scan but NOT
    /// migration-seeded — the user adds it via Settings &gt; Download Clients (no double-grab during
    /// Phase-38 coexistence with the in-process client). No <c>DefaultDefinitions</c> override.
    /// </para>
    /// </summary>
    public class GatewayDownloadClient : DownloadClientBase<GatewayDownloadClientSettings>
    {
        public override string Name => "Mangarr Gateway";
        public override DownloadProtocol Protocol => DownloadProtocol.Http; // Phase 1 D-04

        // D-D hard-default: the gateway always receives outputFormat=cbz (not user-exposed).
        private const string OutputFormat = "cbz";

        private readonly IGatewayDownloadProxy _proxy;

        public GatewayDownloadClient(
            IGatewayDownloadProxy proxy,
            IConfigService configService,
            IDiskProvider diskProvider,
            IRemotePathMappingService remotePathMappingService,
            Logger logger,
            ILocalizationService localizationService)
            : base(configService, diskProvider, remotePathMappingService, logger, localizationService)
        {
            _proxy = proxy;
        }

        // GWDL-02: submit the opaque R6 handle; return the gateway jobId as the DownloadId
        // (idempotent on releaseHandle — NO client-side dedup; Polly may re-submit on 5xx/timeout
        // and the gateway returns the same jobId).
        public override Task<string> Download(RemoteChapter remoteChapter, IIndexer indexer)
        {
            var request = new GatewaySubmitRequest
            {
                // The gateway resolves the release by ReleaseHandle = the opaque R6 DownloadHandle
                // the Phase-37 GatewayIndexer minted (GatewayParser.cs:80 maps it onto
                // Release.DownloadUrl). It is NOT Release.Guid — that is the composite
                // "{sourceKey}:{mangaId}:ch-{n}:{lang}:{chapterId}" id, which the gateway cannot
                // resolve (returns 400 "release no longer resolvable"). (GH #310)
                ReleaseHandle = remoteChapter.Release.DownloadUrl,

                // SourceKey is the ORIGINATING gateway source (mangadex / comix), NOT the Mangarr
                // indexer display name. The gateway mints release guids as "{sourceKey}:..." (the
                // prefix == GatewayRelease.SourceKey); deriving from Guid keeps this correct across
                // the interactive-search cache AND pending-release serialization (Guid is the one
                // field guaranteed to survive both). (GH #310)
                SourceKey = ExtractSourceKey(remoteChapter.Release.Guid),

                OutputFormat = OutputFormat                        // D-D hard-default "cbz"
            };

            var response = _proxy.Submit(request, Settings);

            // GWDL-04: a null jobId is a rejection (the proxy parsed the 400 SubmitResponse body).
            // Throw the release-scoped exception so the release is blocklisted/redownloaded rather
            // than retried as a transport failure.
            if (response?.JobId is null)
            {
                throw new DownloadClientRejectedReleaseException(
                    remoteChapter.Release,
                    response?.Message ?? "Mangarr Gateway rejected the grab");
            }

            return Task.FromResult(response.JobId);
        }

        // The gateway mints release guids as "{sourceKey}:{mangaId}:ch-{chapter}:{lang}:{chapterId}"
        // (GatewayRelease.Guid → ReleaseInfo.Guid). POST /downloads.sourceKey expects the
        // originating source (mangadex / comix) — the first ':'-delimited segment. (GH #310)
        private static string ExtractSourceKey(string guid)
        {
            if (guid.IsNullOrWhiteSpace())
            {
                return guid;
            }

            var separatorIndex = guid.IndexOf(':');
            return separatorIndex > 0 ? guid.Substring(0, separatorIndex) : guid;
        }

        // GWDL-03: map gateway job status → DownloadItemStatus; remap completed OutputPath through
        // IRemotePathMappingService (the container/remote-host trust boundary).
        public override IEnumerable<DownloadClientItem> GetItems()
        {
            foreach (var job in _proxy.GetJobs(Settings).Jobs)
            {
                var status = MapStatus(job.Status);

                var outputPath = status == DownloadItemStatus.Completed && job.OutputPath.IsNotNullOrWhiteSpace()
                    ? _remotePathMappingService.RemapRemoteToLocal(Settings.Host, new OsPath(job.OutputPath))
                    : new OsPath(null);

                yield return new DownloadClientItem
                {
                    DownloadClientInfo = DownloadClientItemClientInfo.FromDownloadClient(this, hasPostImportCategory: false),
                    DownloadId = job.JobId,
                    Title = job.Title,
                    Status = status,

                    // GH #307: map the gateway job's byte counters onto the DownloadClientItem so
                    // the queue projection (MangaQueueService maps Size=TotalSize / SizeLeft=RemainingSize)
                    // and the Activity Queue progress bar render. Without this both stay 0 and the
                    // FE QueueRow `progress = size > 0 ? ... : 0` yields 0 → no bar (Sonarr shows a
                    // full bar for a completed job: TotalBytes>0, RemainingBytes=0).
                    TotalSize = job.TotalBytes,
                    RemainingSize = job.RemainingBytes,

                    OutputPath = outputPath,
                    CanMoveFiles = true,
                    CanBeRemoved = status == DownloadItemStatus.Completed || status == DownloadItemStatus.Failed,

                    // The REAL source the gateway resolved+served this job from (comix / kagane /
                    // mangadex). For a DEFERRED/synthesis release the grab-time Release.Source is
                    // often null (the source is only chosen at resolve time), so this completed-job
                    // sourceKey is the authoritative provenance — threaded to ChapterHistory +
                    // ComicInfo at import so the source is visible without gateway logs.
                    MangaSourceKey = job.SourceKey
                };
            }
        }

        public override void RemoveItem(DownloadClientItem item, bool deleteData)
        {
            // The 404-idempotency lives in the proxy (DIVERGENCE 2); the client does not re-throw.
            _proxy.RemoveJob(item.DownloadId, deleteData, Settings);
        }

        // GWDL-03: resolve every OutputRootFolder through IRemotePathMappingService.
        public override DownloadClientInfo GetStatus()
        {
            var status = _proxy.GetStatus(Settings);

            var info = new DownloadClientInfo
            {
                IsLocalhost = status.IsLocalhost,
                RemovesCompletedDownloads = status.RemovesCompletedDownloads,
                OutputRootFolders = new List<OsPath>()
            };

            foreach (var folder in status.OutputRootFolders)
            {
                info.OutputRootFolders.Add(_remotePathMappingService.RemapRemoteToLocal(Settings.Host, new OsPath(folder)));
            }

            return info;
        }

        // GWDL-01: probe version + auth + output-folder reachability. HARD-fail (Pitfall 3 — overturns
        // spike §7's warn-don't-fail) when a remapped output folder is unreachable/un-writable AND
        // the gateway is NOT localhost (a remote gateway needs a Remote Path Mapping configured).
        protected override void Test(List<ValidationFailure> failures)
        {
            GatewayStatusResponse status;

            try
            {
                // Connectivity + auth + min-version gate. GetVersion throws
                // DownloadClientAuthenticationException / DownloadClientUnavailableException on
                // failure, which the base Test() wrapper converts into a validation failure.
                var version = _proxy.GetVersion(Settings);

                if (version.IsNullOrWhiteSpace())
                {
                    failures.Add(new ValidationFailure(string.Empty, "Mangarr Gateway did not report a version"));
                    return;
                }

                status = _proxy.GetStatus(Settings);
            }
            catch (DownloadClientAuthenticationException ex)
            {
                failures.Add(new ValidationFailure("ApiKey", ex.Message));
                return;
            }
            catch (DownloadClientException ex)
            {
                failures.Add(new ValidationFailure("Host", ex.Message));
                return;
            }

            // Pitfall 3: each output root folder must be host-reachable AND writable when the gateway
            // is remote. On localhost the folders are the gateway's own and need no remap, so a
            // missing folder there is not a Mangarr-side misconfiguration.
            if (status.IsLocalhost)
            {
                return;
            }

            foreach (var folder in status.OutputRootFolders)
            {
                // A remote gateway commonly reports container-internal paths (e.g. "/data/manga")
                // that are NOT valid on the Mangarr host OS until a Remote Path Mapping rewrites
                // them. Two distinct sub-cases must surface the SAME actionable hard-fail rather
                // than an opaque "Test was aborted due to an error":
                //   (a) un-remapped host-invalid path  → TestFolder throws (e.g. ArgumentException
                //       "not a valid Windows path") from the OS path layer;
                //   (b) remapped-but-unreachable folder → TestFolder returns a non-null failure.
                NzbDrone.Core.Validation.NzbDroneValidationFailure outputFailure = null;

                try
                {
                    var local = _remotePathMappingService.RemapRemoteToLocal(Settings.Host, new OsPath(folder));
                    var failure = TestFolder(local.FullPath, "OutputPath");

                    if (failure != null)
                    {
                        outputFailure = new NzbDrone.Core.Validation.NzbDroneValidationFailure(
                            "OutputPath",
                            $"Gateway output folder '{folder}' is not reachable. Configure a Remote Path Mapping so Mangarr can import the delivered CBZ.")
                        {
                            DetailedDescription = failure.ErrorMessage
                        };
                    }
                }
                catch (Exception ex)
                {
                    outputFailure = new NzbDrone.Core.Validation.NzbDroneValidationFailure(
                        "OutputPath",
                        $"Gateway output folder '{folder}' is not reachable. Configure a Remote Path Mapping so Mangarr can import the delivered CBZ.")
                    {
                        DetailedDescription = ex.Message
                    };
                }

                if (outputFailure != null)
                {
                    failures.Add(outputFailure);
                }
            }
        }

        // GWDL-03 status table (spike §5). archiving folds into Downloading (SAB folds verifying/moving);
        // an unknown status defaults to Warning rather than silently dropping the job.
        private static DownloadItemStatus MapStatus(string gatewayStatus) => gatewayStatus switch
        {
            "queued" => DownloadItemStatus.Queued,
            "resolving" => DownloadItemStatus.Queued,
            "downloading" => DownloadItemStatus.Downloading,
            "archiving" => DownloadItemStatus.Downloading,
            "completed" => DownloadItemStatus.Completed,
            "failed" => DownloadItemStatus.Failed,
            "warning" => DownloadItemStatus.Warning,
            "paused" => DownloadItemStatus.Paused,
            _ => DownloadItemStatus.Warning
        };
    }
}
