# System/

## Purpose

System administration UI — status, scheduled tasks, backups, updates, logs. Mostly **media-agnostic** infrastructure that is reusable as-is for Mangarr.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\System\`

**File count**: ~52 files.

## Pages (one subdirectory per route)

### `Status/` — `/system/status`
Read-only dashboard:
| File | Section |
|------|---------|
| `Status.tsx` | Page wrapper |
| `About/About.tsx` | Build version, runtime, OS, paths |
| `About/StartTime.tsx` | App uptime |
| `DiskSpace/DiskSpace.tsx` + `useDiskSpace.ts` | Free space per disk |
| `Health/Health.tsx`, `HealthItemLink.tsx`, `HealthStatus.tsx` + `useHealth.ts` | Health-check results |
| `MoreInfo/MoreInfo.tsx` | External links (docs, source, etc.) |
| `useSystemStatus.ts` | System info hook |

### `Tasks/` — `/system/tasks`
Background task management:
| File | Section |
|------|---------|
| `Tasks.tsx` | Page |
| `Queued/QueuedTasks.tsx`, `QueuedTaskRow.tsx`, `QueuedTaskRowNameCell.tsx` | Currently running/queued commands |
| `Scheduled/ScheduledTasks.tsx`, `ScheduledTaskRow.tsx` | Recurring tasks (next run, last run, interval) |
| `useTasks.ts` | Hook |

### `Backup/` — `/system/backup`
| File | Purpose |
|------|---------|
| `Backups.tsx` | List of available backups |
| `BackupRow.tsx` | Row (name, type, date, size) |
| `RestoreBackupModal.tsx` / `RestoreBackupModalContent.tsx` | Restore confirmation |
| `useBackups.ts` | Hook |

### `Updates/` — `/system/updates`
| File | Purpose |
|------|---------|
| `Updates.tsx` | Update history (version, install date, install status) |
| `UpdateChanges.tsx` | Changelog display |
| `useUpdates.ts` | Hook |

### `Events/` — `/system/events`
| File | Purpose |
|------|---------|
| `LogsTable.tsx` | Server-side paged log entry table |
| `LogsTableRow.tsx` | Row |
| `LogsTableDetailsModal.tsx` | Detail (full message, stack trace) |
| `eventOptionsStore.tsx` | Filter/level options |
| `useEvents.ts` | Hook |

### `Logs/` — `/system/logs/files`
| File | Purpose |
|------|---------|
| `Logs.tsx` | Page |
| `LogFiles.tsx` | Generic log file table |
| `LogFilesTableRow.tsx` | Row |
| `LogsNavMenu.tsx` | Navigate between log types |
| `App/AppLogFiles.tsx` | App logs |
| `Update/UpdateLogFiles.tsx` | Update logs |
| `useLogFiles.ts` | Hook |

## Top-Level Files

| File | Purpose |
|------|---------|
| `useSystem.ts` | Generic system data hook (status, paths, etc.) |

## Manga Adaptation Notes

This module is **fully reusable** for Mangarr. The only change needed is **branding** in the About section:
- "Sonarr Version" → "Mangarr Version"
- Footer "Powered by Sonarr" → "Powered by Mangarr"
- Update links to docs / GitHub repo

The data shapes (system status, health checks, log entries, backups) are entity-agnostic.

## Cross-References

- [../../CLAUDE.md](../../CLAUDE.md) — Frontend overview
- [../../../src/Sonarr.Api.V5/System/](../../../src/Sonarr.Api.V5/System/) — Backend system API
- [../../../src/NzbDrone.Core/HealthCheck/](../../../src/NzbDrone.Core/HealthCheck/) — Backend health
- [../../../src/NzbDrone.Core/Backup/](../../../src/NzbDrone.Core/Backup/) — Backend backups
- [../../../src/NzbDrone.Core/Update/](../../../src/NzbDrone.Core/Update/) — Backend update
- [../../../src/NzbDrone.Core/Jobs/](../../../src/NzbDrone.Core/Jobs/) — Backend scheduled tasks
