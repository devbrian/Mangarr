import ModelBase from 'App/ModelBase';
import useApiQuery from 'Helpers/Hooks/useApiQuery';

const DEFAULT_TAG_DETAILS: TagDetail[] = [];

export interface TagDetail extends ModelBase {
  label: string;
  autoTagIds: number[];
  delayProfileIds: number[];
  downloadClientIds: [];
  importListIds: number[];
  indexerIds: number[];
  notificationIds: number[];
  restrictionIds: number[];
  excludedReleaseProfileIds: number[];
  // Sonarr divergence: Phase 17.3 Plan 17.3-13b fix-forward — backend renamed
  // seriesIds -> mangaIds in Phase 15 D-12 domain rename, but this frontend
  // interface stayed stale until Wave 4 aggregate smoke surfaced the crash
  // ("Cannot read properties of undefined (reading 'length')" in Settings
  // → Tags when any tag exists). Backend `/api/v5/tag/detail` emits `mangaIds`.
  mangaIds: number[];
}

const useTagDetails = () => {
  const { queryKey, ...result } = useApiQuery<TagDetail[]>({
    path: '/tag/detail',
  });

  return {
    ...result,
    data: result.data ?? DEFAULT_TAG_DETAILS,
  };
};

export default useTagDetails;

export const useTagDetail = (id: number) => {
  const { data: tagDetails } = useTagDetails();

  return (
    tagDetails.find((tagDetail) => tagDetail.id === id) ?? {
      delayProfileIds: [],
      importListIds: [],
      notificationIds: [],
      restrictionIds: [],
      excludedReleaseProfileIds: [],
      indexerIds: [],
      downloadClientIds: [],
      autoTagIds: [],
      mangaIds: [],
    }
  );
};
