import migrateAddMangaDefaults from './migrateAddMangaDefaults';

export default function migrate(persistedState) {
  migrateAddMangaDefaults(persistedState);
}
