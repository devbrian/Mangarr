# Calendar/

## Purpose

Calendar view of upcoming/recent episodes (TV concept: "what's airing this week"). For Mangarr, this becomes "what's releasing this week" — using release date predictions or known schedules from MangaDex/MangaUpdates.

**This module needs significant adaptation** because manga release schedules are inherently different from TV airing.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\Calendar\`

## Files

| File | Purpose |
|------|---------|
| `CalendarPage.tsx` | Page wrapper (header, view-toggle, options) |
| `Calendar.tsx` | Main calendar grid component |
| `CalendarFilterModal.tsx` | Series/tag filter |
| `CalendarMissingEpisodeSearchButton.tsx` | "Search all in date range" action |
| `calendarOptionsStore.ts` | Zustand: view mode, options |
| `calendarViews.ts` | View mode definitions (day/week/month/agenda) |
| `getStatusStyle.ts` | Status color/icon for an event |
| `useCalendar.ts` | API hook |

## Subdirectories

| Folder | Purpose |
|--------|---------|
| `Agenda/` | Agenda (list) view |
| `Day/` | Day / week / month grid views (`CalendarDay`, `CalendarDays`, `DayOfWeek`, `DaysOfWeek`) |
| `Events/` | Per-event rendering (`CalendarEvent`, `CalendarEventGroup`, `CalendarEventQueueDetails`) |
| `Header/` | Header with date navigator and view picker |
| `Legend/` | Color legend for status indicators |
| `Options/` | View options popover |
| `iCal/` | iCalendar export modal (`CalendarLinkModal`, `CalendarLinkModalContent`) |

## How It Works

```
User navigates to /calendar (or sets default-home)
    ↓
useCalendar({ start, end, unmonitored }) → GET /api/v5/calendar?start=…&end=…
    ↓
List<CalendarEvent>
    ↓
Group by date / series → render
```

Each event ties an `Episode` to its `airDateUtc`, plus current state:
- Unaired (future)
- Today's airing
- Has file
- Missing (aired but not downloaded)
- In queue (downloading)

## iCal Feed

Backend exposes `/feed/v5/calendar/Sonarr.ics?apikey=…` — users add this to Google Calendar / Apple Calendar etc. The frontend modal generates the URL.

## Manga Adaptation Notes

### Conceptual Changes
- Manga doesn't "air" on a schedule, but most weekly/monthly serializations do have release patterns:
  - Weekly Shounen Jump → every Monday
  - Most monthly mags → 25th-ish
  - Webtoons → variable but often weekly
- Source data:
  - MangaDex publishes "release dates" per chapter
  - MangaUpdates tracks scanlation release dates
  - For ongoing series, can predict future dates from recent cadence

### Implementation Options

**Option A: Keep calendar, change semantics**
- Show next predicted/known chapter release per series
- Group by predicted release week

**Option B: Replace with "Recent Releases" + "Upcoming Predicted Releases"**
- Less rigorous than a calendar but more accurate

**Option C: Hide for v0**
- Skip calendar entirely until prediction model is good enough

### File Renames (when migrated)
| Sonarr | Manga |
|--------|-------|
| `Calendar*` | `Calendar*` (keep) |
| `CalendarMissingEpisodeSearchButton` | `CalendarMissingChapterSearchButton` |
| `CalendarEvent` (Episode-based) | `CalendarEvent` (Chapter-based) |

### Type Changes
- `airDate` → `releaseDate` (predicted or known)
- Add `predicted: boolean` flag for events not based on confirmed data

## Cross-References

- [../../CLAUDE.md](../../CLAUDE.md) — Frontend overview
- [../Episode/CLAUDE.md](../Episode/CLAUDE.md) — Event source
- [../../../src/Sonarr.Api.V5/Calendar/](../../../src/Sonarr.Api.V5/Calendar/) — Backend
