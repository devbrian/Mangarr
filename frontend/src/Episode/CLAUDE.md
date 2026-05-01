# Episode Module

## Purpose

Components and types for managing episodes. Will be **renamed to Chapter** for Mangarr.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\Episode\`

## Key Files

| File | Purpose |
|------|---------|
| `Episode.ts` | Episode type definitions |
| `useEpisode.ts` | API hooks for episodes |
| `EpisodeStatus.tsx` | Status badge component |
| `EpisodeNumber.tsx` | Episode numbering display |
| `EpisodeSearchCell.tsx` | Search action cell |
| `EpisodeQuality.tsx` | Quality display |
| `EpisodeLanguages.tsx` | Language display |
| `EpisodeDetailsModalContent.tsx` | Episode detail popup |

## Episode Type

```typescript
interface Episode {
  id: number;
  seriesId: number;
  seasonNumber: number;
  episodeNumber: number;
  absoluteEpisodeNumber?: number;

  // Scene numbering (for releases)
  sceneSeasonNumber?: number;
  sceneEpisodeNumber?: number;
  sceneAbsoluteEpisodeNumber?: number;

  // Content
  title: string;
  overview?: string;

  // Dates
  airDate?: string;
  airDateUtc?: string;

  // Status
  monitored: boolean;
  hasFile: boolean;
  episodeFileId?: number;

  // Search
  grabbed?: boolean;
  lastSearchTime?: string;
}
```

## API Hooks

```typescript
// Fetch episodes for a series
const { data: episodes } = useEpisodes(seriesId);

// Single episode
const episode = useEpisode(episodeId);

// Toggle monitored
const { mutate } = useToggleEpisodeMonitored(episodeId);
```

## Status Display

```typescript
// EpisodeStatus.tsx shows:
// - Missing (red) - unmonitored or no file
// - Downloaded (green) - has file
// - Downloading (purple) - in queue
// - Grabbed (orange) - grabbed but not imported
// - Unaired (blue) - future air date
```

## Manga Adaptation

### Rename
- Episode → Chapter
- EpisodeFile → ChapterFile
- episodeNumber → chapterNumber
- seasonNumber → volumeNumber (optional)

### Property Changes
```typescript
interface Chapter {
  // Remove
  // airDate, airDateUtc (chapters don't air)
  // sceneSeasonNumber, sceneEpisodeNumber

  // Add/modify
  releaseDate?: string;      // When chapter was released
  chapterNumber: number;     // e.g., 1, 2, 2.5, 45
  volumeNumber?: number;     // Optional volume grouping
  pageCount?: number;        // Number of pages
  scanlationGroup?: string;  // Who translated it
}
```

## Cross-References

- [Series/CLAUDE.md](../Series/CLAUDE.md) - Parent module
- [frontend/CLAUDE.md](../../CLAUDE.md) - Frontend overview
