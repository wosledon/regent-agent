import { afterEach, describe, expect, it, vi } from 'vitest';
import { createRequestScopedApi, createRequestSignal, getActiveRequestSignal } from './requestSignal';

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

describe('createRequestSignal', () => {
  it('marks timeout aborts distinctly and cleans up immediately', async () => {
    vi.useFakeTimers();
    const initialTimerCount = vi.getTimerCount();
    const caller = new AbortController();
    const removeListener = vi.spyOn(caller.signal, 'removeEventListener');
    const handle = createRequestSignal([caller.signal], 1_000);

    expect(vi.getTimerCount()).toBe(initialTimerCount + 1);
    await vi.advanceTimersByTimeAsync(1_000);

    expect(handle.signal.aborted).toBe(true);
    expect(handle.timedOut()).toBe(true);
    expect(handle.signal.reason).toMatchObject({ name: 'TimeoutError' });
    expect(removeListener).toHaveBeenCalledWith('abort', expect.any(Function));
    expect(vi.getTimerCount()).toBe(initialTimerCount);

    caller.abort(new Error('late cancellation'));
    expect(handle.timedOut()).toBe(true);
    handle.dispose();
  });

  it('merges multiple caller signals without allowing a later timeout', () => {
    vi.useFakeTimers();
    const initialTimerCount = vi.getTimerCount();
    const first = new AbortController();
    const second = new AbortController();
    const removeFirst = vi.spyOn(first.signal, 'removeEventListener');
    const removeSecond = vi.spyOn(second.signal, 'removeEventListener');
    const reason = new Error('cancelled by caller');
    const handle = createRequestSignal([first.signal, { signal: second.signal }], 1_000);

    first.abort(reason);

    expect(handle.signal.aborted).toBe(true);
    expect(handle.signal.reason).toBe(reason);
    expect(handle.timedOut()).toBe(false);
    expect(removeFirst).toHaveBeenCalledWith('abort', expect.any(Function));
    expect(removeSecond).toHaveBeenCalledWith('abort', expect.any(Function));
    expect(vi.getTimerCount()).toBe(initialTimerCount);

    vi.advanceTimersByTime(1_000);
    second.abort(new Error('second cancellation'));
    expect(handle.timedOut()).toBe(false);
    handle.dispose();
  });

  it('preserves an already-aborted caller signal and does not start a timer', () => {
    vi.useFakeTimers();
    const initialTimerCount = vi.getTimerCount();
    const caller = new AbortController();
    const reason = new Error('already cancelled');
    const addListener = vi.spyOn(caller.signal, 'addEventListener');
    caller.abort(reason);

    const handle = createRequestSignal([{ signal: caller.signal }], 1_000);

    expect(handle.signal.aborted).toBe(true);
    expect(handle.signal.reason).toBe(reason);
    expect(handle.timedOut()).toBe(false);
    expect(addListener).not.toHaveBeenCalled();
    expect(vi.getTimerCount()).toBe(initialTimerCount);
    handle.dispose();
  });

  it('removes all caller listeners on dispose and is idempotent', () => {
    vi.useFakeTimers();
    const initialTimerCount = vi.getTimerCount();
    const first = new AbortController();
    const second = new AbortController();
    const removeFirst = vi.spyOn(first.signal, 'removeEventListener');
    const removeSecond = vi.spyOn(second.signal, 'removeEventListener');
    const handle = createRequestSignal([first.signal, second.signal], 1_000);

    handle.dispose();
    handle.dispose();

    expect(removeFirst).toHaveBeenCalledOnce();
    expect(removeSecond).toHaveBeenCalledOnce();
    expect(vi.getTimerCount()).toBe(initialTimerCount);
    first.abort();
    second.abort();
    expect(handle.signal.aborted).toBe(false);
  });
});

describe('createRequestScopedApi', () => {
  it('extracts appended TanStack contexts and restores the active signal', () => {
    const queryCaller = new AbortController();
    const mutationCaller = new AbortController();
    const businessCaller = new AbortController();
    const payload = { signal: businessCaller.signal, value: 'business-argument' };
    const scoped = createRequestScopedApi({
      query: () => getActiveRequestSignal(),
      mutation: (id: string) => ({ id, signal: getActiveRequestSignal() }),
      ordinary: (value: { signal: AbortSignal; value: string }) => value,
      fail: () => { throw new Error('failed'); }
    });

    expect(scoped.query(queryCaller.signal)).toBe(queryCaller.signal);
    expect(scoped.query({ signal: queryCaller.signal, queryKey: ['query'], meta: undefined })).toBe(queryCaller.signal);
    expect(scoped.mutation('mutation', {
      signal: mutationCaller.signal,
      mutationKey: ['mutation'],
      meta: undefined
    })).toEqual({ id: 'mutation', signal: mutationCaller.signal });
    expect(scoped.ordinary(payload)).toBe(payload);
    expect(getActiveRequestSignal()).toBeUndefined();

    expect(() => scoped.fail(queryCaller.signal)).toThrow('failed');
    expect(getActiveRequestSignal()).toBeUndefined();
  });
});
