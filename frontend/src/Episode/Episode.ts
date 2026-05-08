// Sonarr divergence: Phase 15 Plan 15-12 — re-export Chapter as Episode.
import Chapter from 'Chapter/Chapter';

interface Episode extends Chapter {
  seasonNumber?: number;
  episodeNumber?: number;
  absoluteEpisodeNumber?: number;
  sceneSeasonNumber?: number;
  sceneEpisodeNumber?: number;
  sceneAbsoluteEpisodeNumber?: number;
  airDate?: string;
  airDateUtc?: string;
  seriesId?: number;
  episodeFileId?: number;
  unverifiedSceneNumbering?: boolean;
}

export default Episode;
