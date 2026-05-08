# NzbDrone.Core/Notifications/Kavita

## Purpose

Kavita manga-reader notification provider — fires a per-library OR scan-all rescan on `ChapterImportedEvent`. Kavita is a popular self-hosted manga/comic reader with first-class API support; this provider lets a Mangarr import flow propagate to a user's Kavita library without manual intervention (NOTIFY-02 + NOTIFY-03).

This directory is a **manga-only sibling** of `Notifications/Komga/` (the other manga reader Mangarr ships in v1) and `Notifications/MediaBrowser/` (Emby/Jellyfin) / `Notifications/Plex/Server/` (Plex Media Server) — same `INotification` plugin shape, same `MediaServerUpdateQueue<T,U>` debounce reuse, manga-shaped event hook.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Notifications\Kavita\`

## Key Files

| File | Purpose |
|------|---------|
| `KavitaNotification.cs` | `INotification` provider — overrides ONLY `OnChapterImport` per Phase 6 D-18; uses `MediaServerUpdateQueue<KavitaNotification, int>` for 5-second debounce keyed on LibraryId (or sentinel 0 for scan-all) |
| `KavitaService.cs` | Service layer — `Test()` connectivity probe via `_proxy.Test()` (forces a fresh JWT fetch to surface bad credentials) |
| `IKavitaProxy.cs` / `KavitaProxy.cs` | HTTP proxy — `Scan()` (POST per-library or scan-all) + `Test()` (force-refresh authenticate); two-step JWT auth with 30-min cache + 401 reauth-and-retry-once |
| `KavitaNotificationSettings.cs` | URL + ApiKey + **OPTIONAL** LibraryId (D-16 — Kavita HAS a scan-all endpoint, unlike Komga) + `KavitaNotificationSettingsValidator` |
| `KavitaAuthResponse.cs` | DTO for `POST /api/Plugin/authenticate` response — `{Token, Username}` |

## Patterns / Conventions

### D-18: OnChapterImport-only event hook

Only `OnChapterImport(ChapterImportMessage)` is overridden. All other `INotification` event hooks (`OnHealthIssue`, `OnHealthRestored`, `OnApplicationUpdate`, `OnMangaAdd`, `OnMangaDelete`, `OnMangaRename`, `OnChapterFileDelete`, `OnChapterFileDeleteForUpgrade`) keep the base-class virtual no-op, and their `Supports*` flags resolve to `false` via the `NotificationBase.HasConcreteImplementation(...)` reflection helper. The TV peers (`OnGrab / OnDownload / OnImportComplete / OnRename / OnSeriesAdd / OnSeriesDelete / OnEpisodeFileDelete / OnManualInteractionRequired`) were trimmed in Phase 15 W-1/W-2. v1.1+ may surface more hooks if Discord/email manga-shaped notifiers come online.

### D-16: LibraryId is OPTIONAL (Kavita HAS scan-all)

Unlike Komga's Pitfall 2 (REQUIRED LibraryId; no scan-all endpoint), Kavita ships both `POST /api/Library/scan?libraryId=N` (per-library) **and** `POST /api/Library/scan-all` (all libraries). `KavitaNotificationSettingsValidator` therefore accepts `LibraryId == null` (= scan-all) and only rejects `LibraryId <= 0` when explicitly set. `KavitaProxy.Scan()` dispatches to whichever endpoint matches the user's configuration.

### Pattern 6: 30-min JWT cache + 401 reauth-and-retry-once (RESEARCH §Don't Hand-Roll row 2)

Kavita uses a two-step plugin auth flow:

1. `POST /api/Plugin/authenticate?apiKey=<key>&pluginName=Mangarr` → `{ "token": "<jwt>", "username": "..." }`
2. Subsequent calls add `Authorization: Bearer <jwt>` header

`KavitaProxy` caches the JWT via Sonarr's `ICacheManager.GetCache<string>(GetType, "kavita-jwt")` with a conservative 30-minute TTL (`TimeSpan.FromMinutes(30)` passed to `_tokenCache.Get(key, factory, ttl)`). On `HttpException` with `HttpStatusCode.Unauthorized`, `ExecuteWithToken` invalidates the cache key, refetches the JWT, and retries the call **exactly once**; a second 401 propagates to the caller (NOT an infinite loop). This handles real expiry, server restarts, and ApiKey rotation without surfacing transient 401s to the user.

The cache key is `{Url}|{ApiKey}` so two Kavita providers configured against different servers — or different API keys on the same server — do not share their JWT.

`KavitaProxy.Test()` always invalidates the cache key first, forcing a fresh authenticate call; this is what surfaces "wrong credentials" or "server unreachable" errors in the Settings UI Test button.

### Pattern 7: Per-LibraryId debounce via MediaServerUpdateQueue

50 chapters arriving in the bulk-add-on-search flow would naively fire 50 Kavita scans. We reuse `MediaServerUpdateQueue<KavitaNotification, int>` (in `Notifications/MediaServerUpdateQueue.cs`) with the **info-only overload** added in Plan 06-10 (`Add(string identifier, TItemInfo info)` + `ProcessQueue(string, Action<TItemInfo>)`). The queue coalesces by `LibraryId` (or sentinel 0 for scan-all), so 50 imports for one library collapse to ONE `_proxy.Scan()` call when `ProcessQueue()` is drained (driven by `NotificationService.HandleAsync(DownloadsProcessedEvent)` etc., 5-second debounce window).

Sentinel 0 is safe because the validator rejects `LibraryId <= 0` when set — only `null` (scan-all) maps to 0 inside this provider's queue, and 0 cannot collide with a real per-library scan request.

### Two-step JWT authentication (NOT X-API-Key, NOT Basic)

Kavita's `Authorization: Bearer <jwt>` contract differs from Komga's `X-API-Key` single-step header. Mangarr stores the API key in `KavitaNotificationSettings.ApiKey` decorated `[FieldDefinition(Privacy = PrivacyLevel.ApiKey)]` (strips at REST serialization, never round-trips to the UI in plaintext). The proxy URL-encodes the ApiKey via `WebUtility.UrlEncode` before placing it in the authenticate query string to handle keys containing `+`, `=`, `&`, etc.

### Per-Url cache identifier

`KavitaNotification.QueueIdentifier(settings) => settings.Url ?? string.Empty` — two Kavita providers configured against different servers do not share their pending-scan queue.

## Manga Adaptation Notes

This is a NEW manga-only directory; nothing to "adapt." Phase 8 cleanup will remove the `// Sonarr divergence:` comments at the top of each file when `Tv/` deletes — the comments document why a manga-shaped sibling exists alongside the TV media-server providers.

If a Mihon/Tachidesk/Suwayomi notification provider ships in v2, it slots in as a sibling directory (`Notifications/Mihon/`) with the same pattern: `Notification` + `Service` + `Proxy` + `Settings` + DTO + `CLAUDE.md`. Auto-discovery via the ThingiProvider reflection scan picks it up at startup.

## Cross-References

- `src/NzbDrone.Core/Notifications/Komga/` — manga-reader sibling shipped in Plan 06-10 (REQUIRED LibraryId, X-API-Key auth, no JWT)
- `src/NzbDrone.Core/Notifications/MediaBrowser/` — TV analog (Emby/Jellyfin) — provider/service/proxy/settings shape
- `src/NzbDrone.Core/Notifications/Plex/PlexTv/` — TV analog token-cache pattern reference (Plex pin flow inspired Pattern 6)
- `src/NzbDrone.Core/Notifications/MediaServerUpdateQueue.cs` — shared debounce queue (Series-coupled overload + info-only overload)
- `src/NzbDrone.Common/Cache/ICacheManager.cs` — token cache primitive
- `src/NzbDrone.Core/Notifications/NotificationBase.cs` — `OnChapterImport` virtual + `SupportsOnChapterImport` reflection helper (added in Plan 06-02)
- `src/NzbDrone.Core/Notifications/NotificationService.cs` — `Handle(ChapterImportedEvent)` fans out via `INotificationFactory.OnChapterImportEnabled()`
- `.planning/phases/06-…/06-CONTEXT.md` D-16/D-17/D-18
- `.planning/phases/06-…/06-RESEARCH.md` Pattern 6 + Example 4 (verbatim KavitaProxy template) + Pattern 7
- `DIVERGENCE.md` Phase 6 entries for the 6 new files
