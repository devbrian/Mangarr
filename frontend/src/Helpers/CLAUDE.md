# Helpers

## Purpose

Custom React hooks and shared prop-type constants used throughout the frontend.

## Directory Structure

```
Helpers/
├── Hooks/                   # Custom React hooks
│   ├── useApiQuery.ts       # React Query wrapper for GET
│   ├── usePagedApiQuery.ts  # React Query wrapper for server-side paged GET
│   ├── useApiMutation.ts    # React Query wrapper for mutations
│   ├── useOptionsStore.ts   # Zustand store factory (createOptionsStore)
│   ├── usePending{Changes,Fields,Items}Store.ts  # Edit-buffer Zustand stores
│   └── … (useDebounce, useMeasure, useKeyboardShortcuts, useTheme, …)
├── Props/                   # Prop-type constant modules (icons, kinds, sizes,
│   │                        #   align, sortDirections, inputTypes, filter*Types…)
│   └── index.js             # Barrel re-export
├── createPersist.ts         # localStorage persist helper for Zustand
└── DragType.ts              # react-dnd item types
```

Command execution lives in `Commands/useCommands.ts` (sibling dir), not here.

## Key Hooks

### useApiQuery

Wrapper around React Query's `useQuery` for API GET requests:

```typescript
import { useApiQuery } from 'Helpers/Hooks/useApiQuery';

const { data, isLoading, isFetched, error } = useApiQuery<Manga[]>({
  path: '/manga',
  queryOptions: { staleTime: 5 * 60 * 1000 },  // 5 minutes
});
```

Features:
- Automatic API key injection
- Configurable stale time
- Type-safe responses
- Error handling

### useApiMutation

Wrapper for API mutations (POST, PUT, DELETE):

```typescript
import { useApiMutation } from 'Helpers/Hooks/useApiMutation';

const { mutate, isPending, error } = useApiMutation<Manga>({
  method: 'PUT',
  path: `/manga/${id}`,
});

// Execute mutation
mutate({ ...manga, monitored: true });
```

Features:
- Automatic cache invalidation
- Validation error parsing
- Loading state tracking

### useOptionsStore

Factory for creating Zustand stores with localStorage persistence:

```typescript
import { createOptionsStore } from 'Helpers/Hooks/useOptionsStore';

const useMyOptions = createOptionsStore('my_options', {
  view: 'grid',
  sortBy: 'name',
});
```

### useCommands

Execute backend commands and track progress:

```typescript
import { useExecuteCommand, useCommandExecuting } from 'Commands/useCommands';

const executeCommand = useExecuteCommand();
const isSearching = useCommandExecuting(CommandNames.MangaSearch);

// Execute
executeCommand({
  name: CommandNames.MangaSearch,
  mangaId: 123,
});
```

## Helper Functions

### addOrUpdateQueryClientItem

Update React Query cache after mutation:

```typescript
import { addOrUpdateQueryClientItem } from 'Helpers/Hooks/useApiMutation';

addOrUpdateQueryClientItem(queryClient, ['/manga'], updatedManga);
```

### getValidationFailures

Extract validation errors from API response:

```typescript
import { getValidationFailures } from 'Helpers/Hooks/useApiMutation';

const failures = getValidationFailures(error);
// Returns: { fieldName: ['error message'] }
```

## Props Utilities

### icons

Icon name constants:

```typescript
import { icons } from 'Helpers/Props';

<Icon name={icons.REFRESH} />
<Icon name={icons.SEARCH} />
<Icon name={icons.ADD} />
<Icon name={icons.DELETE} />
<Icon name={icons.EDIT} />
```

## Cross-References

- [frontend/CLAUDE.md](../../CLAUDE.md) - Frontend overview
- [Manga/CLAUDE.md](../Manga/CLAUDE.md) - Uses these hooks (Sonarr `Series/` deleted Phase 17.3 Plan 17.3-13)
- [Store/CLAUDE.md](../Store/CLAUDE.md) - Redux integration
