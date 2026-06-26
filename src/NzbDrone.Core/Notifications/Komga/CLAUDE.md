# NzbDrone.Core/Notifications/Komga

## Purpose

Komga manga-reader notification provider — fires a per-library rescan on `ChapterImportedEvent`. Komga is the most popular self-hosted manga reader; this provider lets a Mangarr import flow propagate to a user's Komga library without manual intervention (NOTIFY-01 + NOTIFY-03).

It is a **manga-only sibling** of `Notifications/Kavita/` (the only other live notifier). Its shape derives from the Sonarr media-server notifiers (Emby/Jellyfin `MediaBrowser/`, Plex `Plex/Server/`) — same `INotification` plugin shape, same `MediaServerUpdateQueue<T,U>` debounce reuse, manga-shaped event hook — but those TV providers are NOT in the live tree (reference-preserved per the policy; see Cross-References).


## Key Files

| File | Purpose |
|------|---------|
| `KomgaNotification.cs` | `INotification` provider — overrides ONLY `OnChapterImport` per Phase 6 D-18; uses `MediaServerUpdateQueue<KomgaNotification, int>` for 5-second debounce keyed on LibraryId |
| `KomgaService.cs` | Service layer — `Test()` connectivity probe via `GET /api/v1/libraries` (also seeds Phase 7 Settings dropdown) |
| `IKomgaProxy.cs` / `KomgaProxy.cs` | HTTP proxy — `Scan()` (POST per-library scan) + `GetLibraries()` (GET library list); X-API-Key header auth (Komga 1.20.0+) |
| `KomgaNotificationSettings.cs` | URL + ApiKey + **REQUIRED** LibraryId (Pitfall 2 — Komga has NO scan-all endpoint) + `KomgaNotificationSettingsValidator` |
| `KomgaLibrary.cs` | DTO for `GET /api/v1/libraries` response — `{Id, Name, Root}` |

## Patterns / Conventions

### D-18: OnChapterImport-only event hook

Only `OnChapterImport(ChapterImportMessage)` is overridden. All other `INotification` event hooks (`OnHealthIssue`, `OnHealthRestored`, `OnApplicationUpdate`, `OnMangaAdd`, `OnMangaDelete`, `OnMangaRename`, `OnChapterFileDelete`, `OnChapterFileDeleteForUpgrade`) keep the base-class virtual no-op, and their `Supports*` flags resolve to `false` via the `NotificationBase.HasConcreteImplementation(...)` reflection helper. The TV peers (`OnGrab / OnDownload / OnImportComplete / OnRename / OnSeriesAdd / OnSeriesDelete / OnEpisodeFileDelete / OnManualInteractionRequired`) were trimmed in Phase 15 W-1/W-2. v1.1+ may surface more hooks if Discord/email manga-shaped notifiers come online.

### Pitfall 2: LibraryId is REQUIRED (Komga has NO scan-all endpoint)

`KomgaNotificationSettingsValidator` enforces `RuleFor(c => c.LibraryId).NotNull().GreaterThan(0)`. Per [komga.org/docs/openapi/library-scan/](https://komga.org/docs/openapi/library-scan/), only `POST /api/v1/libraries/{libraryId}/scan` exists in Komga 1.20.0+ — there is no global `POST /api/v1/scan` endpoint. CONTEXT.md D-15 originally inferred otherwise; the Plan 06-10 correction note is appended in CONTEXT.md for traceability. `KomgaProxy.Scan()` also defensively throws `InvalidOperationException` if LibraryId is missing (validator should have blocked the save first).

### Pattern 7: Per-LibraryId debounce via MediaServerUpdateQueue

50 chapters arriving in the bulk-add-on-search flow would naively fire 50 Komga scans. We reuse `MediaServerUpdateQueue<KomgaNotification, int>` (in `Notifications/MediaServerUpdateQueue.cs`) with the **info-only overload** added in Plan 06-10 (`Add(string identifier, TItemInfo info)` + `ProcessQueue(string, Action<TItemInfo>)`). The queue coalesces by `LibraryId`, so 50 imports for one library collapse to ONE `_proxy.Scan()` call when `ProcessQueue()` is drained (driven by `NotificationService.HandleAsync(DownloadsProcessedEvent)` etc., 5-second debounce window). The Series-coupled `Add(string, Series, TItemInfo)` overload still exists unchanged for TV media servers (Plex, Emby, Xbmc).

### X-API-Key authentication

Komga 1.20.0+ supports per-user API keys via *User Settings → API Keys → Generate*. Mangarr stores the key in `KomgaNotificationSettings.ApiKey` decorated `[FieldDefinition(Privacy = PrivacyLevel.ApiKey)]`, which strips at REST serialization and never round-trips to the UI in plaintext. The proxy adds `X-API-Key: <key>` to every request — NOT `Authorization: Bearer ...` and NOT HTTP Basic. The key is never `_logger.Trace`-d directly; structured log redaction at NLog layer + `PrivacyLevel.ApiKey` field convention together cover ASVS V8 / V9.

### Per-Url cache identifier

`KomgaNotification.QueueIdentifier(settings) => settings.Url ?? string.Empty` — two Komga providers configured against different servers do not share their pending-scan queue.

## Manga Adaptation Notes

This is a NEW manga-only directory; nothing to "adapt." Phase 8 cleanup will remove the `// Sonarr divergence:` comments at the top of each file when `Tv/` deletes — the comments document why a manga-shaped sibling exists alongside the TV media-server providers.

If a Mihon/Tachidesk/Suwayomi notification provider ships in v2, it slots in as a sibling directory (`Notifications/Mihon/`) with the same pattern: `Notification` + `Service` + `Proxy` + `Settings` + DTO + `CLAUDE.md`. Auto-discovery via the ThingiProvider reflection scan picks it up at startup.

## Cross-References

- `src/NzbDrone.Core/Notifications/Kavita/` — the only other live notifier (manga-reader sibling; OPTIONAL LibraryId, JWT auth)
- Sonarr `Notifications/MediaBrowser/` (Emby/Jellyfin) + `Notifications/Plex/Server/` — shape antecedents (debounce + per-Settings cache identifier pattern); NOT in the live tree at HEAD — reference-preserved under `.planning/reference/sonarr-vertical-slices/notifications-extra/` (Komga + Kavita are the only live notifiers)
- `src/NzbDrone.Core/Notifications/MediaServerUpdateQueue.cs` — shared debounce queue (Series-coupled overload + info-only overload)
- `src/NzbDrone.Core/Notifications/NotificationBase.cs` — `OnChapterImport` virtual + `SupportsOnChapterImport` reflection helper (added in Plan 06-02)
- `src/NzbDrone.Core/Notifications/NotificationService.cs` — `Handle(ChapterImportedEvent)` fans out via `INotificationFactory.OnChapterImportEnabled()`
- `.planning/phases/06-…/06-CONTEXT.md` D-15/D-17/D-18 + Plan 06-10 correction note
- `.planning/phases/06-…/06-RESEARCH.md` Pitfall 2 + Pattern 7 + Open Questions Q-1
- `DIVERGENCE.md` Phase 6 entries for the 6 new files
