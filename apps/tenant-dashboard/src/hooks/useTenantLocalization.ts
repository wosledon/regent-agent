import { useQuery } from '@tanstack/react-query';
import { api } from '../lib/api-client/api';

export function useTenantLocalization(enabled = true) {
  return useQuery({
    queryKey: ['localization'],
    queryFn: api.localization,
    enabled,
    staleTime: 60_000
  });
}
