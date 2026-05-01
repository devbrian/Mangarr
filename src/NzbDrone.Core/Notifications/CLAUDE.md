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
    public abstract void OnEpisodeFileDelete(EpisodeDeleteMessage deleteMessage);
    public abstract void OnSeriesAdd(SeriesAddMessage message);
    public abstract void OnSeriesDelete(SeriesDeleteMessage message);
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
| `OnEpisodeFileDelete` | Rename to `OnChapterFileDelete` |
| `OnSeriesAdd` / `OnSeriesDelete` | Rename to `OnMangaAdd` / `OnMangaDelete` |
| Provider-specific text formatting | Update `BuildMessage` helpers in each provider |

The bulk of provider code (HTTP plumbing, settings, validation) stays the same.

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — NzbDrone.Core overview
- [../Messaging/CLAUDE.md](../Messaging/CLAUDE.md) — Events that trigger notifications
- [../ThingiProvider/](../ThingiProvider/) — Provider plugin base
- [../HealthCheck/](../HealthCheck/) — Source of `HealthCheck` events
