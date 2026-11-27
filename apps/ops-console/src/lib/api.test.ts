import { afterEach, describe, expect, it, vi } from 'vitest';
import { DEFAULT_REQUEST_TIMEOUT_MS } from '@regent/ui-shared';
import { api, ApiError, OPS_PERMISSION_DENIED_EVENT, OPS_UNAUTHORIZED_EVENT } from './api';

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

let randomValue = 0;

function secureRandomValues<T extends ArrayBufferView | null>(array: T): T {
  if (array instanceof Uint8Array) {
    randomValue += 1;
    array.fill(randomValue);
  }
  return array;
}

function abortableFetch() {
  return vi.fn((_input: RequestInfo | URL, init?: RequestInit) => new Promise<Response>((_resolve, reject) => {
    const signal = init?.signal;
    if (!signal) {
      reject(new Error('Missing request signal.'));
      return;
    }
    if (signal.aborted) {
      reject(signal.reason);
      return;
    }
    signal.addEventListener('abort', () => reject(signal.reason), { once: true });
  }));
}

describe('Operations API transport', () => {
  it('bootstraps and logs in through fetch without crypto.randomUUID', async () => {
    vi.stubGlobal('crypto', { getRandomValues: secureRandomValues });
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ display_name: 'Demo Operator' }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ email: 'operator@example.test', display_name: 'Demo Operator' }), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await api.login();
    await api.me();

    expect(fetchMock.mock.calls[0][0]).toBe('/auth/dev-login');
    expect(fetchMock.mock.calls[1][0]).toBe('/ops/auth/me');
    expect(new Headers(fetchMock.mock.calls[1][1].headers).get('X-Request-Id')).toMatch(/^[0-9a-f-]{36}$/);
  });

  it('exposes stable status and code metadata without backend exception text', async () => {
    vi.stubGlobal('crypto', { getRandomValues: secureRandomValues });
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      error: { code: 'INTERNAL_ERROR', message: 'private backend detail' }
    }), { status: 503, headers: { 'Content-Type': 'application/json' } })));

    const error = await api.login().catch((cause: unknown) => cause);
    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 503, code: 'INTERNAL_ERROR', message: 'Request failed: 503' });
    expect((error as Error).message).not.toContain('private backend detail');
  });

  it('maps request timeouts to ApiError status 0 and TIMEOUT', async () => {
    vi.useFakeTimers();
    vi.stubGlobal('crypto', { getRandomValues: secureRandomValues });
    const fetchMock = abortableFetch();
    vi.stubGlobal('fetch', fetchMock);

    const pending = api.me();
    const rejection = expect(pending).rejects.toMatchObject({ status: 0, code: 'TIMEOUT' });
    const signal = fetchMock.mock.calls[0][1]?.signal;
    expect(signal).toBeInstanceOf(AbortSignal);

    await vi.advanceTimersByTimeAsync(DEFAULT_REQUEST_TIMEOUT_MS);
    await rejection;

    expect(signal?.aborted).toBe(true);
  });

  it('keeps stable idempotency keys for identical write retries only', async () => {
    vi.stubGlobal('crypto', { getRandomValues: secureRandomValues });
    const fetchMock = vi.fn<typeof fetch>(async () => new Response(JSON.stringify({}), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await api.refreshStatus();
    await api.refreshStatus();
    await api.createIncident('Same incident', 'SEV3', 'First message');
    await api.createIncident('Same incident', 'SEV3', 'First message');
    await api.createIncident('Same incident', 'SEV3', 'Different message');

    const keys = fetchMock.mock.calls.map(([, init]) => new Headers(init?.headers).get('Idempotency-Key'));
    expect(keys[0]).toMatch(/^[0-9a-f-]{36}$/);
    expect(keys[1]).toBe(keys[0]);
    expect(keys[3]).toBe(keys[2]);
    expect(keys[4]).not.toBe(keys[2]);
  });

  it('extracts If-Match from versioned bodies and falls back to the wildcard', async () => {
    vi.stubGlobal('crypto', { getRandomValues: secureRandomValues });
    const fetchMock = vi.fn<typeof fetch>(async () => new Response(JSON.stringify({}), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await api.setTenantStatus('tenant-if-match', 'Active');
    await api.saveBackupProfile({
      id: 'profile-if-match',
      deploymentId: 'deployment-1',
      enabled: true,
      components: 'postgres',
      retentionDays: 7,
      version: 9
    });

    const fallbackHeaders = new Headers(fetchMock.mock.calls[0][1]?.headers);
    const versionedHeaders = new Headers(fetchMock.mock.calls[1][1]?.headers);
    expect(fallbackHeaders.get('If-Match')).toBe('*');
    expect(versionedHeaders.get('If-Match')).toBe('"9"');
  });

  it('cleans up request timers and caller listeners when header preparation fails', async () => {
    vi.useFakeTimers();
    vi.stubGlobal('crypto', { getRandomValues: secureRandomValues });
    const initialTimerCount = vi.getTimerCount();
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
    const caller = new AbortController();
    const addListener = vi.spyOn(caller.signal, 'addEventListener');
    const removeListener = vi.spyOn(caller.signal, 'removeEventListener');
    const invalidHeader = {
      toString() { throw new Error('invalid header value'); }
    };

    await expect(api.resolveAlert('alert-1', invalidHeader as unknown as string, caller.signal))
      .rejects.toThrow('invalid header value');

    expect(fetchMock).not.toHaveBeenCalled();
    expect(addListener).toHaveBeenCalledWith('abort', expect.any(Function), { once: true });
    expect(removeListener).toHaveBeenCalledWith('abort', expect.any(Function));
    expect(vi.getTimerCount()).toBe(initialTimerCount);
  });

  it('keeps restore and alert authority on the authenticated server session', async () => {
    vi.stubGlobal('crypto', { getRandomValues: secureRandomValues });
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ id: 'restore' }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ id: 'restore' }), { status: 200 }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ id: 'silence' }), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await api.requestRestore(['backup-1'], 'Tenant');
    await api.approveRestore('restore');
    await api.acknowledgeAlert('alert-1');
    await api.resolveAlert('alert-1', 'Recovered and verified by SRE');
    await api.silenceAlert('fingerprint-1', 'Maintenance', 60);

    const requestBody = JSON.parse(fetchMock.mock.calls[0][1].body as string) as unknown;
    const approvalBody = JSON.parse(fetchMock.mock.calls[1][1].body as string) as unknown;
    const silenceBody = JSON.parse(fetchMock.mock.calls[4][1].body as string) as unknown;
    const resolutionHeaders = new Headers(fetchMock.mock.calls[3][1].headers);
    expect(requestBody).toEqual({ backupIds: ['backup-1'], scope: 'Tenant' });
    expect(approvalBody).toEqual({ resolution: 'Isolated local manifest verification passed' });
    expect(fetchMock.mock.calls[2][1].body).toBeUndefined();
    expect(fetchMock.mock.calls[3][1].body).toBeUndefined();
    expect(resolutionHeaders.get('X-Resolution')).toBe('Recovered and verified by SRE');
    expect(silenceBody).toEqual({ fingerprint: 'fingerprint-1', reason: 'Maintenance', minutes: 60 });
  });

  it('dispatches the centralized 401 event', async () => {
    vi.stubGlobal('crypto', { getRandomValues: secureRandomValues });
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 401 })));
    const listener = vi.fn();
    window.addEventListener(OPS_UNAUTHORIZED_EVENT, listener);

    await expect(api.me()).rejects.toThrow('401');
    expect(listener).toHaveBeenCalledOnce();
    window.removeEventListener(OPS_UNAUTHORIZED_EVENT, listener);
  });

  it('dispatches the centralized 403 event', async () => {
    vi.stubGlobal('crypto', { getRandomValues: secureRandomValues });
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 403 })));
    const listener = vi.fn();
    window.addEventListener(OPS_PERMISSION_DENIED_EVENT, listener);

    await expect(api.me()).rejects.toMatchObject({ status: 403 });
    expect(listener).toHaveBeenCalledOnce();
    window.removeEventListener(OPS_PERMISSION_DENIED_EVENT, listener);
  });
});
