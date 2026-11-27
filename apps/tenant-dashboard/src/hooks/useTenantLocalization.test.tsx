import type { PropsWithChildren } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useTenantLocalization } from './useTenantLocalization';

const apiMocks = vi.hoisted(() => ({ localization: vi.fn() }));
vi.mock('../lib/api-client/api', () => ({ api: apiMocks }));

function wrapper(client: QueryClient) {
  return function Wrapper({ children }: PropsWithChildren) {
    return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
  };
}

beforeEach(() => {
  apiMocks.localization.mockReset();
  apiMocks.localization.mockResolvedValue({ locale: 'en-US', timeZone: 'UTC', currency: 'USD', version: 1 });
});

describe('useTenantLocalization', () => {
  it('deduplicates consumers through the shared localization query key', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const { result } = renderHook(() => ({ first: useTenantLocalization(), second: useTenantLocalization() }), {
      wrapper: wrapper(client)
    });
    await waitFor(() => expect(result.current.first.data?.timeZone).toBe('UTC'));
    expect(result.current.second.data).toEqual(result.current.first.data);
    expect(apiMocks.localization).toHaveBeenCalledOnce();
    expect(client.getQueryData(['localization'])).toEqual(result.current.first.data);
  });

  it('does not fetch while disabled', () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    renderHook(() => useTenantLocalization(false), { wrapper: wrapper(client) });
    expect(apiMocks.localization).not.toHaveBeenCalled();
  });
});
