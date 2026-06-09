// quick-260608-vf9 follow-up — read-side CustomFormatProfile list hook, the
// CustomFormatProfile peer of Settings/Profiles/Translations/useTranslationProfiles.ts.
// Powers CustomFormatProfileSelectInput (the import-list / bulk-edit select).
// Wires /api/v5/customformatprofile (Phase 5 Plan 05-03). The full
// create/edit/delete surface stays owned by the CustomFormatProfile editor's
// direct useApiMutation calls — this hook is read-only.
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { CustomFormatProfileResource } from './CustomFormatProfile';

const PATH = '/customformatprofile';

export const useCustomFormatProfiles = () => {
  const result = useApiQuery<CustomFormatProfileResource[]>({
    path: PATH,
    queryOptions: {
      gcTime: Infinity,
      staleTime: 5 * 60 * 1000,
    },
  });

  return {
    ...result,
    data: result.data ?? ([] as CustomFormatProfileResource[]),
  };
};

export const useCustomFormatProfilesData = () => {
  const { data } = useCustomFormatProfiles();

  return data;
};
