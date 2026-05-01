# Components (Shared UI)

## Purpose

Reusable UI components used across the application. These are **generic and can be reused as-is** for Mangarr.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\Components\`

## Directory Structure

```
Components/
├── Page/                    # Page layout
│   ├── Page.tsx
│   ├── PageContent.tsx
│   ├── PageContentBody.tsx
│   ├── Toolbar/             # Toolbar components
│   ├── Header/              # Page header
│   └── Sidebar/             # Navigation sidebar
├── Table/                   # Table components
│   ├── Table.tsx
│   ├── TableBody.tsx
│   ├── TableHeader.tsx
│   ├── TableHeaderCell.tsx
│   ├── TableRow.tsx
│   ├── TableRowCell.tsx
│   └── VirtualTable*.tsx    # Virtual scrolling variants
├── Form/                    # Form components
│   ├── FormGroup.tsx
│   ├── FormLabel.tsx
│   ├── FormInputGroup.tsx
│   └── various inputs...
├── Modal/                   # Modal dialogs
│   ├── Modal.tsx
│   └── ModalContent.tsx
├── Alert/                   # Alert messages
├── Card/                    # Card containers
├── Icon/                    # Icon component
├── Label/                   # Labels/badges
├── Link/                    # Link components
├── Loading/                 # Loading indicators
├── Menu/                    # Dropdown menus
├── Filter/                  # Filter UI
├── FileBrowser/             # File browser
├── ProgressBar.tsx          # Progress bar
├── CircularProgressBar.tsx  # Circular progress
├── MonitorToggleButton.tsx  # Monitor toggle
├── HeartRating.tsx          # Rating display
├── DescriptionList/         # Key-value lists
├── FieldSet/                # Fieldset container
└── Error/                   # Error displays
```

## Key Components

### ProgressBar
```typescript
<ProgressBar
  progress={75}
  kind="primary"    // primary, success, warning, danger
  showText={true}
  text="75%"
/>
```

### MonitorToggleButton
```typescript
<MonitorToggleButton
  monitored={true}
  isDisabled={false}
  onPress={() => toggleMonitor()}
/>
```

### Table
```typescript
<Table>
  <TableHeader>
    <TableHeaderCell>Title</TableHeaderCell>
  </TableHeader>
  <TableBody>
    <TableRow>
      <TableRowCell>Content</TableRowCell>
    </TableRow>
  </TableBody>
</Table>
```

### Modal
```typescript
<Modal isOpen={isOpen} onClose={onClose}>
  <ModalContent onClose={onClose}>
    {/* Modal content */}
  </ModalContent>
</Modal>
```

### Page Layout
```typescript
<Page>
  <PageToolbar>
    {/* Toolbar buttons */}
  </PageToolbar>
  <PageContent>
    <PageContentBody>
      {/* Main content */}
    </PageContentBody>
  </PageContent>
</Page>
```

## Form Components

| Component | Purpose |
|-----------|---------|
| `TextInput` | Text input field |
| `NumberInput` | Numeric input |
| `CheckInput` | Checkbox |
| `SelectInput` | Dropdown select |
| `PathInput` | File path input |
| `TagInput` | Tag selection |
| `QualityProfileSelectInput` | Quality profile picker |

## Icon Usage

```typescript
import { icons } from 'Helpers/Props';

<Icon name={icons.REFRESH} />
<Icon name={icons.SEARCH} />
<Icon name={icons.EDIT} />
<Icon name={icons.DELETE} />
```

## CSS Modules

All components use CSS modules:
```typescript
import styles from './ProgressBar.module.css';

<div className={styles.container}>
  <div className={styles.progress} />
</div>
```

## Reusability

These components are **domain-agnostic** and require no changes for Mangarr:
- All layout components
- All form components
- Tables, modals, menus
- Progress indicators
- Icons and labels

## Cross-References

- [frontend/CLAUDE.md](../../CLAUDE.md) - Frontend overview
- [Series/CLAUDE.md](../Series/CLAUDE.md) - Feature using these components
