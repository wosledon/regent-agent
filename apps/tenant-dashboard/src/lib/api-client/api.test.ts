import { afterEach, describe, expect, it, vi } from 'vitest';
import { DEFAULT_REQUEST_TIMEOUT_MS } from '@regent/ui-shared';
import { api, TENANT_PERMISSION_DENIED_EVENT, TENANT_UNAUTHORIZED_EVENT } from './api';

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

function secureRandomValues<T extends ArrayBufferView | null>(array: T): T {
  if (array instanceof Uint8Array) {
    for (let index = 0; index < array.length; index += 1) array[index] = index + 1;
  }
  return array;
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
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

describe('Tenant API transport without crypto.randomUUID', () => {
  it('sends authentication, dashboard reads and writes through fetch', async () => {
    vi.stubGlobal('crypto', { getRandomValues: secureRandomValues });
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse({ email: 'owner@example.test' }))
      .mockResolvedValueOnce(jsonResponse({ recovery_codes: [] }))
      .mockResolvedValueOnce(jsonResponse({ companyName: 'Example' }))
      .mockResolvedValueOnce(jsonResponse({ id: 'project-1' }, 201));
    vi.stubGlobal('fetch', fetchMock);

    await api.passwordLogin('owner@example.test', 'strong-password-123');
    await api.register('new@example.test', 'strong-password-123', 'New Owner', 'New Co');
    await api.dashboard();
    await api.createProject({ name: 'Project', owner: 'Owner', mission: 'Deliver value', targetDays: 30, budgetMinor: 1000, currency: 'CNY' });

    expect(fetchMock).toHaveBeenCalledTimes(4);
    for (const [path, init] of fetchMock.mock.calls) {
      expect(path).toMatch(/^\/(auth|v1)\//);
      expect(init.credentials).toBe('include');
      const headers = new Headers(init.headers);
      expect(headers.get('X-Request-Id')).toMatch(/^[0-9a-f-]{36}$/);
    }
    const writeHeaders = new Headers(fetchMock.mock.calls[3][1].headers);
    expect(writeHeaders.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
  });

  it('still reaches auth fetch and omits optional request id when no CSPRNG exists', async () => {
    vi.stubGlobal('crypto', {});
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ email: 'owner@example.test' }));
    vi.stubGlobal('fetch', fetchMock);

    await api.passwordLogin('owner@example.test', 'strong-password-123');

    expect(fetchMock).toHaveBeenCalledOnce();
    expect(new Headers(fetchMock.mock.calls[0][1].headers).has('X-Request-Id')).toBe(false);
  });

  it('maps request timeouts to ApiError status 0 and TIMEOUT', async () => {
    vi.useFakeTimers();
    vi.stubGlobal('crypto', { getRandomValues: secureRandomValues });
    const fetchMock = abortableFetch();
    vi.stubGlobal('fetch', fetchMock);

    const pending = api.dashboard();
    const rejection = expect(pending).rejects.toMatchObject({ status: 0, code: 'TIMEOUT' });
    const signal = fetchMock.mock.calls[0][1]?.signal;
    expect(signal).toBeInstanceOf(AbortSignal);

    await vi.advanceTimersByTimeAsync(DEFAULT_REQUEST_TIMEOUT_MS);
    await rejection;

    expect(signal?.aborted).toBe(true);
  });

  it('propagates caller cancellation through the merged request signal', async () => {
    vi.stubGlobal('crypto', { getRandomValues: secureRandomValues });
    const fetchMock = abortableFetch();
    vi.stubGlobal('fetch', fetchMock);
    const caller = new AbortController();
    const reason = new Error('cancelled by caller');

    const pending = api.project('project-1', caller.signal);
    const rejection = expect(pending).rejects.toBe(reason);
    const signal = fetchMock.mock.calls[0][1]?.signal;
    expect(signal).toBeInstanceOf(AbortSignal);
    expect(signal).not.toBe(caller.signal);

    caller.abort(reason);
    await rejection;

    expect(signal?.aborted).toBe(true);
  });

  it('preserves idempotency and If-Match header semantics', async () => {
    vi.stubGlobal('crypto', { getRandomValues: secureRandomValues });
    const fetchMock = vi.fn<typeof fetch>(async () => jsonResponse({}));
    vi.stubGlobal('fetch', fetchMock);

    await api.updateLocalization({ locale: 'zh-CN', timeZone: 'UTC', currency: 'CNY', version: 4 });
    await api.updateDisplaySuffix('Owner');
    await api.decideApproval('approval-1', { decision: 'Approve', reason: 'Verified' }, 'caller-idempotency-key');

    const versionedHeaders = new Headers(fetchMock.mock.calls[0][1]?.headers);
    const fallbackHeaders = new Headers(fetchMock.mock.calls[1][1]?.headers);
    const callerHeaders = new Headers(fetchMock.mock.calls[2][1]?.headers);
    expect(versionedHeaders.get('If-Match')).toBe('"4"');
    expect(fallbackHeaders.get('If-Match')).toBe('*');
    expect(callerHeaders.get('Idempotency-Key')).toBe('caller-idempotency-key');
  });

  it('dispatches one centralized 401 event without replacing the fetch error', async () => {
    vi.stubGlobal('crypto', { getRandomValues: secureRandomValues });
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ error: { code: 'UNAUTHENTICATED' } }, 401)));
    const listener = vi.fn();
    window.addEventListener(TENANT_UNAUTHORIZED_EVENT, listener);

    await expect(api.dashboard()).rejects.toMatchObject({ status: 401 });
    expect(listener).toHaveBeenCalledOnce();
    window.removeEventListener(TENANT_UNAUTHORIZED_EVENT, listener);
  });

  it('dispatches one centralized 403 event without replacing the fetch error', async () => {
    vi.stubGlobal('crypto', { getRandomValues: secureRandomValues });
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ error: { code: 'FORBIDDEN' } }, 403)));
    const listener = vi.fn();
    window.addEventListener(TENANT_PERMISSION_DENIED_EVENT, listener);

    await expect(api.dashboard()).rejects.toMatchObject({ status: 403, code: 'FORBIDDEN' });
    expect(listener).toHaveBeenCalledOnce();
    window.removeEventListener(TENANT_PERMISSION_DENIED_EVENT, listener);
  });
});
