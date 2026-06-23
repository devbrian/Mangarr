# Mangarr 1.0.0

The first public release of **Mangarr** — a manga / manhwa / manhua library manager and downloader.

Mangarr monitors sources for new chapters of the titles you follow, then automatically downloads, sorts, and organizes them into your library — and can upgrade chapters when a better scan becomes available. It's built on the mature [Sonarr](https://github.com/Sonarr/Sonarr) foundation, adapted for the manga domain.

## Highlights

- **Library management** — add and organize manga/manhwa/manhua, browse your collection, and track chapter status.
- **Metadata** — title metadata sourced from MangaDex / AniList / MyAnimeList.
- **Automatic monitoring & search** — Mangarr watches for new and missing chapters and grabs them via the external manga gateway.
- **Organized downloads** — finished chapters are imported, named, and filed into your library as CBZ.
- **Translation profiles & custom formats** — prefer specific scanlation languages/groups and define quality preferences.
- **Import lists** — bring in titles from MangaDex, AniList, and MyAnimeList.
- **Notifications** — Komga and Kavita integrations to keep your reader in sync.
- **Modern stack** — .NET 10 backend, React 18 + TypeScript UI, SQLite or PostgreSQL.

## Install

Container images are published to GHCR:

```
docker pull ghcr.io/devbrian/mangarr:1.0.0
```

The app listens on port **8989** by default. Per-runtime archives are attached to this release.

## Notes

This is an early release — please report issues at https://github.com/devbrian/Mangarr/issues.
