# NzbDrone.Core/Notifications

## Purpose

Notification provider plugins — fire alerts to users on events like episode grabbed, episode imported, health degraded, etc. Uses the **ThingiProvider** plugin pattern.

This directory is **media-agnostic** — only message text references "episodes/series" and will need updating to "chapters/manga." The 25+ notification providers (Discord, Slack, Email, Telegram, etc.) all work identically for manga.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Notifications\`

## Top-Level Files

| File | Purpose |
|------|---------|
| `INotification.cs` / `NotificationBase.cs` | Provider base |
| `INotificationFactory.cs` / `NotificationFactory.cs` | ThingiProvider factory |
| `INotificationRepository.cs` / `NotificationRepository.cs` | DB persistence |
| `NotificationDefinition.cs` | Persisted config (link to Settings JSON) |
| `NotificationService.cs` | Orchestrator — listens to events, fans out to providers |
| `NotificationStatusService.cs` | Track health of each notification provider |
| `GrabMessage.cs` | Message DTO sent on grab events |
| `EpisodeDownloadMessage.cs` | Sent on import |
| `EpisodeDeleteMessage.cs` | Sent on delete |
| `SeriesAddMessage.cs`, `SeriesDeleteMessage.cs` | Series-level events |
| `HealthCheck/HealthMessage.cs` | Health change |
| `ImportFailureMessage.cs`, `ManualInteractionRequiredMessage.cs` | Failures |

## Provider Subdirectories (25+)

| Folder | Service |
|--------|---------|
| `Apprise/` | Apprise gateway |
| `Boxcar/` | Boxcar push |
| `CustomScript/` | Run a user-supplied script |
| `Discord/` | Discord webhook |
| `Email/` | SMTP email |
| `Emby/` | Emby media server |
| `Gotify/` | Gotify push |
| `Join/` | Join (joaoapps) |
| `Jellyfin/` (sometimes under Emby) | Jellyfin |
| `Mailgun/` | Mailgun email API |
| `MediaBrowser/` (Emby) | Emby/Jellyfin notification |
| `Notifiarr/` | Notifiarr passthrough |
| `Ntfy/` | ntfy.sh |
| `Plex/` | Plex Media Server (refresh library) |
| `Prowl/` | Prowl iOS push |
| `PushBullet/` | PushBullet |
| `Pushcut/` | Pushcut iOS |
| `Pushover/` | Pushover push |
| `Synology/` | DSM notification |
| `Telegram/` | Telegram bot |
| `SendGrid/` | SendGrid email |
| `Signal/` | Signal Messenger |
| `Simplepush/` | Simplepush |
| `Slack/` | Slack webhook |
| `Trakt/` | Trakt scrobble |
| `Twitter/` | Twitter (X) |
| `Webhook/` | Generic JSON webhook |
| `Xbmc/` (Kodi) | Kodi |
| `Subsonic/` (?) | (not always present) |

## Event Hooks (NotificationBase)

```csharp
public abstract class NotificationBase<TSettings> : INotification
    where TSettings : NotificationSettingsBase<TSettings>, new()
{
    public abstract void OnGrab(GrabMessage grabMessage);
    public abstract void OnDownload(EpisodeDownloadMessage message);
    public abstract void OnRename(Series series, List<RenamedEpisodeFile> renamedFiles);
    public abstract void OnHealthIssue(HealthCheck.HealthCheck healthCheck);
    public abstract void OnHealthRestored(HealthCheck.HealthCheck previousCheck);
    public abstract void OnApplicationUpdate(ApplicationUpdateMessage message);
    public abstract void OnManualInteractionRequired(ManualInteractionRequiredMessage message);
    // … per-event toggles via Settings
}
```

Each provider overrides only the hooks it cares about (and the `Supports*` flags it sets).

## Adding a New Notification Provider

1. Create folder `Notifications/MyService/`.
2. `MyServiceSettings.cs` (settings + validator + base URL/token fields).
3. `MyService.cs` extends `NotificationBase<MyServiceSettings>`.
4. Override the event hooks you support. Set `OnGrabEnabled`, `OnDownloadEnabled` etc. in your `Settings`.
5. Implement `Test()` to validate connectivity.
6. Auto-discovered. Add tests under `NzbDrone.Core.Test/NotificationTests/MyServiceTests/`.

## Triggering Notifications

`NotificationService` is the orchestrator. It implements `IHandle<EpisodeGrabbedEvent>`, `IHandle<EpisodeImportedEvent>`, etc., and on each event:
1. Loads enabled `NotificationDefinition` rows
2. Filters by tags (per-series tag matching)
3. Calls the appropriate `On*` method on each

## Manga Adaptation Notes

| Concern | Action |
|---------|--------|
| Message text references "Episode" / "Series" | Update message-builder methods to use "Chapter" / "Manga" |
| `OnGrab` signature uses `GrabMessage` | The DTO references EpisodeGrabbedEvent → adjust to ChapterGrabbedEvent |
| `OnRename` uses `RenamedEpisodeFile` | Rename to `RenamedChapterFile` |
| Provider-specific text formatting | Update `BuildMessage` helpers in each provider |

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
