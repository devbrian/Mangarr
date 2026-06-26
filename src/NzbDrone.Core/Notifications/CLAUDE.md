# NzbDrone.Core/Notifications

## Purpose

Notification provider plugins — fire alerts to users on events like episode grabbed, episode imported, health degraded, etc. Uses the **ThingiProvider** plugin pattern.

**Komga and Kavita are the ONLY live notification providers** (both manga-reader rescan triggers — see their subdirectory CLAUDE.md files). Sonarr's other notifiers (Discord, Slack, Email, Telegram, etc.) are NOT in the live tree — they are reference-preserved fork heritage under `.planning/reference/sonarr-vertical-slices/notifications-extra/` per the Reference Preservation Policy. The contract surface (`INotification` / `NotificationBase`) was trimmed to manga-shape in Phase 15 W-1/W-2.


## Top-Level Files

| File | Purpose |
|------|---------|
| `INotification.cs` / `NotificationBase.cs` | Provider base (interface + abstract base) |
| `NotificationFactory.cs` | ThingiProvider factory (`INotificationFactory` declared in-file) |
| `NotificationRepository.cs` | DB persistence (`INotificationRepository` declared in-file) |
| `NotificationDefinition.cs` | Persisted config (link to Settings JSON) |
| `NotificationSettingsBase.cs` | Base for provider settings |
| `NotificationService.cs` | Orchestrator — listens to events, fans out to providers |
| `NotificationStatus.cs` / `NotificationStatusRepository.cs` / `NotificationStatusService.cs` | Track health of each notification provider |
| `MediaServerUpdateQueue.cs` | Shared debounce queue (Komga/Kavita rescan coalescing) |
| `MetadataLinkType.cs` / `NotificationMetadataLink.cs` | Metadata-link DTOs |
| `ApplicationUpdateMessage.cs` | DTO for `OnApplicationUpdate` |
| `ChapterImportMessage.cs` | DTO for `OnChapterImport` (Phase 6 D-18) |
| `ChapterFileDeleteMessage.cs` | DTO for `OnChapterFileDelete` / `…ForUpgrade` |
| `MangaAddMessage.cs` / `MangaDeleteMessage.cs` | DTOs for `OnMangaAdd` / `OnMangaDelete` (Phase 8 Plan 99-08) |

## Provider Subdirectories

Only the two live manga-reader providers ship in the tree; the 25+ Sonarr notifiers are reference-preserved heritage (see Purpose above), NOT live subdirectories here.

| Folder | Service |
|--------|---------|
| `Komga/` | Komga manga-reader library rescan (`OnChapterImport`) |
| `Kavita/` | Kavita manga-reader library rescan (`OnChapterImport`) |

## Event Hooks (NotificationBase)

Phase 15 W-1/W-2 trimmed `INotification` to manga-shape only. The current contract:

```csharp
public abstract class NotificationBase<TSettings> : INotification
    where TSettings : NotificationSettingsBase<TSettings>, new()
{
    // Health + lifecycle (media-agnostic — kept from Sonarr verbatim)
    public virtual void OnHealthIssue(HealthCheck.HealthCheck healthCheck) { }
    public virtual void OnHealthRestored(HealthCheck.HealthCheck previousCheck) { }
    public virtual void OnApplicationUpdate(ApplicationUpdateMessage message) { }

    // Phase 6 D-18 — manga import (Komga / Kavita rescan trigger)
    public virtual void OnChapterImport(ChapterImportMessage message) { }

    // Phase 8 Plan 99-08 — manga library-state hooks (siblings of OnSeriesAdd/Delete/Rename).
    // v1: surface-only — no provider override; v1.1+ Discord/email/webhook providers opt in.
    public virtual void OnMangaAdd(MangaAddMessage message) { }
    public virtual void OnMangaDelete(MangaDeleteMessage deleteMessage) { }
    public virtual void OnMangaRename(Manga.Manga manga, List<RenamedChapterFile> renamedFiles) { }

    // Manga siblings of TV-deleted OnEpisodeFileDelete / OnEpisodeFileDeleteForUpgrade.
    // v1: surface-only — no v1 publisher; v1.1+ providers opt in.
    public virtual void OnChapterFileDelete(ChapterFileDeleteMessage deleteMessage) { }
    public virtual void OnChapterFileDeleteForUpgrade(ChapterFileDeleteMessage deleteMessage) { }

    // Reflection-backed Supports* flags resolve to TRUE iff the concrete provider overrides
    // the matching method (HasConcreteImplementation in NotificationBase.cs:HasConcreteImplementation).
}
```

Each provider overrides only the hooks it cares about; the `Supports*` flags reflect that automatically via the `HasConcreteImplementation` reflection helper.

## Adding a New Notification Provider

1. Create folder `Notifications/MyService/`.
2. `MyServiceSettings.cs` (settings + validator + base URL/token fields).
3. `MyService.cs` extends `NotificationBase<MyServiceSettings>`.
4. Override the event hooks you support. Set `OnGrabEnabled`, `OnDownloadEnabled` etc. in your `Settings`.
5. Implement `Test()` to validate connectivity.
6. Auto-discovered. Add tests under `NzbDrone.Core.Test/NotificationTests/MyServiceTests/`.

## Triggering Notifications

`NotificationService` is the orchestrator. It implements `IHandle<ChapterImportedEvent>` (and the manga lifecycle events), and on each event:
1. Loads enabled `NotificationDefinition` rows
2. Filters by tags (per-manga tag matching)
3. Calls the appropriate `On*` method on each

## Manga Adaptation Notes

The TV→manga rename of the contract surface is complete:
- `INotification` + `NotificationBase` trimmed of TV hooks (Phase 15 W-1/W-2).
- `OnSeriesAdd / OnSeriesDelete / OnEpisodeFileDelete / OnEpisodeFileDeleteForUpgrade` columns dropped from the `Notifications` schema (Phase 15 D-22 / Plan 15-02 — see `Datastore/Migration/001_mangarr_baseline.cs:96`).
- Manga peers shipped: `OnChapterImport` (Phase 6 D-18), `OnMangaAdd / OnMangaDelete / OnMangaRename` (Phase 8 Plan 99-08), `OnChapterFileDelete / OnChapterFileDeleteForUpgrade` (post-15 surface backfill).
- `RenamedChapterFile` exists at `MediaFiles/RenamedChapterFile.cs`; `ChapterImportMessage`, `MangaAddMessage`, `MangaDeleteMessage`, `ChapterFileDeleteMessage` exist alongside the contract.

The bulk of provider code (HTTP plumbing, settings, validation) stays the same.

## Phase 6 Manga Siblings

These manga-side artifacts ship under this directory and the `INotification` cross-cut. Each is a parallel sibling to a TV analog; **Phase 8 cleanup** will collapse the sibling pairs when `Tv/` deletes.

| Sibling | Phase 6 Plan | Notes |
|---------|--------------|-------|
| [`Notifications/Komga/`](./Komga/CLAUDE.md) | Plan 06-10 | Komga manga-reader rescan. `INotification` provider implementing ONLY `OnChapterImport` per D-18; `MediaServerUpdateQueue<KomgaNotification, int>` 5-second debounce per `LibraryId` (Pattern 7); `X-API-Key` auth (Komga 1.20.0+); LibraryId REQUIRED per Pitfall 2 (Komga has NO scan-all endpoint). Phase 8 cleanup: collapse the `// Sonarr divergence:` header comment when `Tv/` deletes. |
| [`Notifications/Kavita/`](./Kavita/CLAUDE.md) | Plan 06-11 | Kavita manga-reader rescan. `INotification` provider implementing ONLY `OnChapterImport` per D-18; two-step Bearer JWT auth with 30-min `ICacheManager` cache + 401-reauth-and-retry-once (Pattern 6 + Example 4); LibraryId OPTIONAL per D-16 (null → `scan-all`); per-`Settings.Url` cache identifier. Phase 8 cleanup: collapse with TV equivalent when `Tv/` deletes. |
| `Notifications/ChapterImportMessage.cs` | Plan 06-02 | Manga-shaped POCO (sibling of `DownloadMessage`); carries `Manga + Chapter + ChapterFile + SourceTitle + SourcePath + DownloadClient + DownloadId + OldFiles`. Phase 8 cleanup: collapse with `DownloadMessage`. |
| `INotification.OnChapterImport` extension + `NotificationBase` virtual no-op + `NotificationService` `IHandle<ChapterImportedEvent>` fan-out + `INotificationFactory.OnChapterImportEnabled()` factory dual-filter | Plan 06-02 | RESEARCH §Pitfall 7 three-layer mitigation (interface + base virtual + service fan-out). Reflection-backed `SupportsOnChapterImport` reuses the existing `HasConcreteImplementation` helper at `NotificationBase.cs:122` so providers that DO override return true and providers that DON'T (every TV notification) return false — safe by default. `NotificationDefinition.OnChapterImport` defaults to `TRUE`. Phase 8 cleanup: collapse with `OnImportComplete` when domain rename runs. |
| `MediaServerUpdateQueue<T,U>` info-only overloads | Plan 06-10 | Existing Series-coupled `Add(string, Series, TItemInfo)` API doesn't fit manga readers — added `Add(string, TItemInfo) + ProcessQueue(string, Action<TItemInfo>)` overloads alongside (Rule 3). Backed by a separate rolling-cache key (`"pendingInfo"` vs `"pendingSeries"`) so the two paths never alias. Existing TV callers (Plex, Emby/Jellyfin, Xbmc) compile + behave unchanged. Phase 8 cleanup: when the Series-coupled overload retires alongside `Tv/`, the info-only overload becomes the canonical surface. |

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — NzbDrone.Core overview
- [../Messaging/CLAUDE.md](../Messaging/CLAUDE.md) — Events that trigger notifications
- [../ThingiProvider/](../ThingiProvider/) — Provider plugin base
- [../HealthCheck/](../HealthCheck/) — Source of `HealthCheck` events
