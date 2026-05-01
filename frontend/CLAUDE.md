# Frontend

## Purpose

React-based web UI for the application. Built with TypeScript, using Redux + Zustand for state management and React Query for API data fetching.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\`

## Technology Stack

| Technology | Version | Purpose |
|------------|---------|---------|
| React | 18.3.1 | UI framework |
| TypeScript | 5.7.2 | Type safety |
| Redux | 4.2.1 | Global state |
| Zustand | 5.0.3 | Feature state |
| React Query | 5.61.0 | API data fetching |
| React Router | 5.2.0 | Client-side routing |
| Webpack | 5 | Bundling |
| PostCSS | - | CSS processing |

## Directory Structure

```
frontend/
├── src/
│   ├── App/                 # App root, routing, providers
│   ├── Series/              # Series feature (→ Manga)
│   ├── Episode/             # Episode components (→ Chapter)
│   ├── Season/              # Season components (→ Volume)
│   ├── Components/          # Shared UI components
│   ├── Store/               # Redux store
│   ├── Helpers/             # Hooks and utilities
│   ├── typings/             # TypeScript definitions
│   ├── Settings/            # Settings pages
│   ├── Activity/            # Activity/monitoring
│   ├── Calendar/            # Calendar view
│   ├── Wanted/              # Wanted items
│   ├── AddSeries/           # Add series flow
│   └── Content/             # Static assets
├── build/
│   └── webpack.config.js    # Webpack configuration
├── .eslintrc.js             # ESLint rules
├── .prettierrc.json         # Prettier config
└── tsconfig.json            # TypeScript config
```

## Key Files

| File | Purpose |
|------|---------|
| `src/App/App.tsx` | Root component with providers |
| `src/App/AppRoutes.tsx` | Route definitions |
| `src/index.ts` | Application entry point |
| `src/bootstrap.tsx` | Bootstrap initialization |
| `build/webpack.config.js` | Build configuration |

## State Management

### 1. Redux (Global State)
```typescript
// Store/createAppStore.js
// Used for: settings, custom filters, app-wide state
import { useSelector } from 'react-redux';

const settings = useSelector(state => state.settings);
```

### 2. Zustand (Feature State)
```typescript
// Series/seriesOptionsStore.ts
// Used for: view preferences, filters, sorting
const useSeriesOptions = create(
  persist(
    (set) => ({
      view: 'posters',
      setView: (view) => set({ view }),
    }),
    { name: 'series_options' }
  )
);
```

### 3. React Query (API State)
```typescript
// Helpers/Hooks/useApiQuery.ts
// Used for: API data fetching and caching
const { data, isLoading } = useApiQuery({
  queryKey: ['/series'],
});
```

## API Integration

### useApiQuery
```typescript
import { useApiQuery } from 'Helpers/Hooks/useApiQuery';

const { data, isLoading, isFetched } = useApiQuery<Series[]>({
  queryKey: ['/series'],
  staleTime: 5 * 60 * 1000, // 5 minutes
});
```

### useApiMutation
```typescript
import { useApiMutation } from 'Helpers/Hooks/useApiMutation';

const { mutate, isPending } = useApiMutation<Series>({
  method: 'PUT',
  queryKey: ['/series', id],
});

mutate({ ...series, monitored: true });
```

## Component Conventions

### Feature Module Structure
```
Series/
├── Series.ts                 # Type definitions
├── useSeries.ts              # API hooks
├── seriesOptionsStore.ts     # Zustand store
├── Index/                    # List view
│   ├── SeriesIndex.tsx
│   ├── Posters/              # Poster view
│   ├── Overview/             # Overview view
│   └── Table/                # Table view
├── Details/                  # Detail view
│   └── SeriesDetails.tsx
├── Edit/                     # Edit modal
└── Delete/                   # Delete modal
```

### CSS Modules
```typescript
// Styles are CSS modules with .module.css extension
import styles from './SeriesIndex.module.css';

<div className={styles.container}>
```

## Routing

```typescript
// Key routes in AppRoutes.tsx
/                       → SeriesIndex (home)
/series/:titleSlug      → SeriesDetails
/add/new                → AddNewSeries
/add/import             → ImportSeriesPage
/calendar               → Calendar
/activity/history       → History
/activity/queue         → Queue
/wanted/missing         → Missing
/settings/*             → Settings pages
/system/*               → System pages
```

## Build Commands

```bash
# Development build
yarn build

# Production build
yarn build --env production

# Clean build output
yarn clean

# Lint
yarn lint

# Format
yarn format
```

## Output

Build output goes to: `../_output/UI/`

## Cross-References

- [PROJECT_CONTEXT.md](../PROJECT_CONTEXT.md) - Overall architecture
- [Series/CLAUDE.md](./src/Series/CLAUDE.md) - Series feature module
- [Sonarr.Api.V5/CLAUDE.md](../src/Sonarr.Api.V5/CLAUDE.md) - Backend API
