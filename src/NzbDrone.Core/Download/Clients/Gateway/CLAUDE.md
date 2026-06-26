# Download/Clients/Gateway

## Purpose

Phase 38 (GWDL-01..04) — the **"Mangarr Gateway"** out-of-process `IDownloadClient`. Submits the
opaque release handle the Phase-37 `GatewayIndexer` minted (R6) to the external manga-gateway's
`POST /downloads`, tracks the returned `jobId` as the `DownloadId` (idempotent), maps gateway job
status to `DownloadItemStatus`, and resolves `OutputPath`/`OutputRootFolders` through
`IRemotePathMappingService`. The Phase-36 monitoring loop drives the grab → poll → import lifecycle
unchanged.


## Key Files

| File | Purpose |
|------|---------|
| `GatewayDownloadClient.cs` | The `IDownloadClient` impl. Extends `DownloadClientBase<GatewayDownloadClientSettings>` directly (manga = `DownloadProtocol.Http`, Phase 1 D-04). State-mapping + `GetStatus` + `Test` (no HTTP — that lives in the proxy). |
| `GatewayDownloadClientSettings.cs` | Connectivity Settings (Host/Port/UseSsl/UrlBase/ApiKey) + validator. `ApiKey` masked via `PrivacyLevel.ApiKey`. Extends `DownloadClientSettingsBase<T>` (free memberwise equality). NO `outputFormat` field (D-D hard-default `cbz`). |
| `IGatewayDownloadProxy.cs` | One method per endpoint: `Submit` / `GetJobs` / `GetJob` / `RemoveJob` / `GetStatus` / `GetVersion`. |
| `GatewayDownloadProxy.cs` | All HTTP I/O. `X-Api-Key` header on every call; `BuildBaseUrl` + `SuppressHttpError`; exception ladder mirroring SAB's `ProcessRequest`/`CheckForError`. |
| `Responses/*.cs` | Hand-written Newtonsoft POCOs mirroring the frozen OpenAPI schemas (`SubmitRequest`, `SubmitResponse`, `Job`, `JobList`, `StatusResponse`, `Version`). |

## Patterns / Conventions

- **SABnzbd three-file decomposition** — `Settings` / `IProxy`+`Proxy` / `Client`. Structural mirror
  of `origin/v5-develop:src/NzbDrone.Core/Download/Clients/Sabnzbd/*` (the
  sonarr-consistency-audit diff target).
- **Mangarr-shape ctor** — the client takes `IGatewayDownloadProxy` + the five base services
  (`IConfigService`, `IDiskProvider`, `IRemotePathMappingService`, `Logger`,
  `ILocalizationService`), the manga-shape ctor pattern the now-retired `InProcessImageDownloadClient`
  established (NOT SAB's `UsenetClientBase(httpClient, …)`). The client gets **no** `IHttpClient`.
- **Status table (`MapStatus`)** — `queued`/`resolving` → Queued; `downloading`/`archiving` →
  Downloading; `completed` → Completed; `failed` → Failed; `warning` → Warning; `paused` → Paused;
  unknown → Warning.
- **`Test()` hard-fail (Pitfall 3)** — probes version + auth + per-`OutputRootFolder`
  reachability. HARD-fails (not Warn) with "configure a Remote Path Mapping" when a remapped folder
  is unreachable AND `isLocalhost == false`. Localhost folders are the gateway's own and skip the
  check.
- **Connectivity fields DUPLICATED** — the Phase-37 `Indexers/Gateway/GatewaySettings.cs` shape is
  duplicated verbatim; a shared `GatewaySettingsBase` is deliberately NOT extracted (Sonarr
  duplicates per provider).

## The TWO deliberate divergences from the SAB template

Both are cited inline with `// Sonarr divergence:` + `Sabnzbd/SabnzbdProxy.cs` provenance; the
sonarr-consistency-audit reads these.

1. **Submit 400-quirk (GWDL-04 / Pitfall 1)** — `POST /downloads` returning HTTP **400** carries a
   `SubmitResponse` body (NOT the standard `Error` envelope). `GatewayDownloadProxy.Submit` parses
   the body as `GatewaySubmitResponse` for BOTH 200 AND 400 and RETURNS it (`JobId == null` on
   rejection); it does NOT throw a transport error. The **client** translates `jobId == null` →
   `DownloadClientRejectedReleaseException` so the release is blocklisted/redownloaded. Only 401/5xx
   take the exception ladder.
2. **DELETE-404 idempotency** — `DELETE /downloads/{jobId}` returning **404** (already removed) is
   SWALLOWED as success (the monitor may call `RemoveItem` twice). SAB has no analog.

## Disabled-by-default contract

The client is **auto-discovered** by the ThingiProvider reflection scan but **NOT
migration-seeded** — it has no auto-enabled `DefaultDefinitions` and no seed row in
`001_mangarr_baseline.cs`. The user adds it via **Settings → Download Clients**. Phase 39 retired the
in-process client, so the gateway client is now the **sole** download path.

## Cross-References

- [src/NzbDrone.Core/Indexers/Gateway/GatewaySettings.cs](../../../Indexers/Gateway/GatewaySettings.cs) — Phase-37 connectivity-field shape (duplicated, not shared)
- [src/NzbDrone.Core/Indexers/Gateway/GatewayParser.cs](../../../Indexers/Gateway/GatewayParser.cs) — line 80, `DownloadUrl = downloadHandle` (the R6 handoff)
- _(historical)_ `Download/Clients/InProcess/InProcessImageDownloadClient.cs` — established the manga-shape ctor + `DownloadProtocol.Http` precedent; **deleted in Phase 39** (Plans 39-01/02)
- [src/NzbDrone.Core/Download/DownloadClientBase.cs](../../DownloadClientBase.cs) — `Test()`/`TestFolder`/`RetryStrategy` + injected services
- [.planning/spikes/manga-gateway.openapi.yaml](../../../../../.planning/spikes/manga-gateway.openapi.yaml) — the frozen download contract
- [.planning/phases/38-gatewaydownloadclient-comicinfo-injector/38-01-PLAN.md](../../../../../.planning/phases/38-gatewaydownloadclient-comicinfo-injector/38-01-PLAN.md) — this plan
