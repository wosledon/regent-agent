import { act, renderHook } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useTenantHub } from './useTenantHub';

const signalr = vi.hoisted(() => ({
  start: vi.fn(),
  stop: vi.fn(),
  on: vi.fn(),
  onreconnecting: vi.fn(),
  onreconnected: vi.fn(),
  onclose: vi.fn(),
  withUrl: vi.fn(),
  withAutomaticReconnect: vi.fn(),
  configureLogging: vi.fn(),
  build: vi.fn()
}));

vi.mock('@microsoft/signalr', () => ({
  LogLevel: { Warning: 'Warning' },
  HubConnectionBuilder: class {
    withUrl = signalr.withUrl;
    withAutomaticReconnect = signalr.withAutomaticReconnect;
    configureLogging = signalr.configureLogging;
    build = signalr.build;
  }
}));

beforeEach(() => {
  vi.useFakeTimers();
  Object.values(signalr).forEach(mock => mock.mockReset());
  signalr.withUrl.mockReturnValue(signalr);
  signalr.withAutomaticReconnect.mockReturnValue(signalr);
  signalr.configureLogging.mockReturnValue(signalr);
  signalr.build.mockReturnValue(signalr);
  signalr.stop.mockResolvedValue(undefined);
});

afterEach(() => vi.useRealTimers());

describe('useTenantHub initial connection', () => {
  it('retries an initial failure with backoff and invalidates snapshots after recovery', async () => {
    signalr.start.mockRejectedValueOnce(new Error('offline')).mockResolvedValueOnce(undefined);
    let reconnected: (() => void) | undefined;
    signalr.onreconnected.mockImplementation((callback: () => void) => {
      reconnected = callback;
      return signalr;
    });
    const client = new QueryClient();
    const invalidate = vi.spyOn(client, 'invalidateQueries').mockResolvedValue();
    const { result } = renderHook(() => useTenantHub(true), {
      wrapper: ({ children }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>
    });

    await act(async () => { await vi.advanceTimersByTimeAsync(0); });
    expect(result.current).toBe('disconnected');
    expect(signalr.start).toHaveBeenCalledOnce();

    await act(async () => { await vi.advanceTimersByTimeAsync(1000); });
    expect(result.current).toBe('connected');
    expect(signalr.start).toHaveBeenCalledTimes(2);

    await act(async () => { reconnected?.(); });
    expect(invalidate).toHaveBeenCalled();
  });
});
