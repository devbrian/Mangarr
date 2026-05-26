# Helpers

## Purpose

Custom React hooks and utility functions used throughout the frontend.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\Helpers\`

## Directory Structure

```
Helpers/
├── Hooks/                   # Custom React hooks
│   ├── useApiQuery.ts       # React Query wrapper for GET
│   ├── useApiMutation.ts    # React Query wrapper for mutations
│   ├── useOptionsStore.ts   # Zustand store factory
│   ├── useCommands.ts       # Command execution
│   └── various hooks...
└── Props/                   # Prop type utilities
    └── icons.ts             # Icon name constants
```

## Key Hooks

### useApiQuery

Wrapper around React Query's `useQuery` for API GET requests:

```typescript
import { useApiQuery } from 'Helpers/Hooks/useApiQuery';

const { data, isLoading, isFetched, error } = useApiQuery<Series[]>({
  queryKey: ['/series'],
  staleTime: 5 * 60 * 1000,  // 5 minutes
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

const { mutate, isPending, error } = useApiMutation<Series>({
  method: 'PUT',
  queryKey: ['/series', id],
});

// Execute mutation
mutate({ ...series, monitored: true });
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
const isSearching = useCommandExecuting(CommandNames.SeriesSearch);

// Execute
executeCommand({
  name: CommandNames.SeriesSearch,
  seriesId: 123,
});
```

## Helper Functions

### addOrUpdateQueryClientItem

Update React Query cache after mutation:

```typescript
import { addOrUpdateQueryClientItem } from 'Helpers/Hooks/useApiMutation';

addOrUpdateQueryClientItem(queryClient, ['/series'], updatedSeries);
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
