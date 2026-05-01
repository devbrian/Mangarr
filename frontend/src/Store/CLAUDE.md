# Store (Redux)

## Purpose

Redux store configuration and state management infrastructure for global application state.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\Store\`

## Directory Structure

```
Store/
├── createAppStore.js        # Store factory
├── Actions/                 # Redux actions
│   ├── actionTypes.js       # Action type constants
│   ├── baseActions.js       # Common action creators
│   ├── settingsActions.js   # Settings actions
│   ├── customFilterActions.js
│   ├── captchaActions.js
│   └── Creators/            # Action creator factories
├── Middleware/              # Redux middleware
├── Selectors/               # Memoized selectors
│   ├── createClientSideCollectionSelector.js
│   ├── createSettingsSectionSelector.js
│   └── selectSettings.js
└── Migrators/               # State migration utilities
```

## Store Configuration

```javascript
// createAppStore.js
import { createStore, applyMiddleware } from 'redux';
import thunk from 'redux-thunk';

const store = createStore(
  rootReducer,
  applyMiddleware(thunk, ...middlewares)
);
```

## State Shape

```typescript
interface RootState {
  settings: SettingsState;
  customFilters: CustomFilter[];
  commands: Command[];
  app: AppState;
  // ... other slices
}
```

## Actions Pattern

```javascript
// Action types
export const SET_SETTING_VALUE = 'SET_SETTING_VALUE';
export const SAVE_SETTINGS = 'SAVE_SETTINGS';

// Action creators
export const setSettingValue = (section, name, value) => ({
  type: SET_SETTING_VALUE,
  payload: { section, name, value }
});

// Thunk actions
export const saveSettings = (section) => (dispatch, getState) => {
  dispatch({ type: SAVE_SETTINGS_PENDING });
  // API call...
};
```

## Selectors

```javascript
// Memoized selectors for performance
import { createSelector } from 'reselect';

export const selectSettings = createSelector(
  (state) => state.settings,
  (settings) => settings.ui
);

// Client-side collection with filtering/sorting
export const createClientSideCollectionSelector = () =>
  createSelector(
    selectItems,
    selectFilter,
    selectSort,
    (items, filter, sort) => {
      // Filter and sort logic
    }
  );
```

## Usage

```typescript
import { useSelector, useDispatch } from 'react-redux';
import { setSettingValue } from 'Store/Actions/settingsActions';

// Read state
const settings = useSelector(state => state.settings);

// Dispatch actions
const dispatch = useDispatch();
dispatch(setSettingValue('ui', 'theme', 'dark'));
```

## Note on State Management

This project uses a **hybrid approach**:
- **Redux**: Global settings, commands, filters
- **Zustand**: Feature-specific view options (see `seriesOptionsStore.ts`)
- **React Query**: API data fetching and caching

## Cross-References

- [frontend/CLAUDE.md](../../CLAUDE.md) - Frontend overview
- [Series/CLAUDE.md](../Series/CLAUDE.md) - Feature using both Redux and Zustand
