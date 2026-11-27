export const DEFAULT_REQUEST_TIMEOUT_MS = 30_000;

export type RequestSignalSource = AbortSignal | { signal: AbortSignal } | null | undefined;

export type RequestScopedApi<T> = {
  [K in keyof T]: T[K] extends (...args: infer A) => infer R
    ? (...args: [...A, context?: unknown]) => R
    : T[K];
};

let activeRequestSignal: AbortSignal | undefined;

export function getActiveRequestSignal(): AbortSignal | undefined {
  return activeRequestSignal;
}

function contextSignal(context: unknown): AbortSignal | undefined {
  return isRequestSignalSource(context) ? sourceSignal(context) : undefined;
}

export function createRequestScopedApi<T extends object>(methods: T): RequestScopedApi<T> {
  return new Proxy(methods, {
    get(target, property, receiver) {
      const value = Reflect.get(target, property, receiver);
      if (typeof value !== 'function') return value;
      return (...args: unknown[]) => {
        const context = args.at(-1);
        const signal = args.length > value.length ? contextSignal(context) : undefined;
        const callArgs = signal ? args.slice(0, -1) : args;
        const previous = activeRequestSignal;
        activeRequestSignal = signal;
        try {
          return Reflect.apply(value, target, callArgs);
        } finally {
          activeRequestSignal = previous;
        }
      };
    }
  }) as RequestScopedApi<T>;
}

export interface RequestSignalHandle {
  readonly signal: AbortSignal;
  dispose(): void;
  timedOut(): boolean;
}

function sourceSignal(source: RequestSignalSource): AbortSignal | undefined {
  if (!source) return undefined;
  return isAbortSignal(source) ? source : source.signal;
}

function isAbortSignal(value: unknown): value is AbortSignal {
  return typeof AbortSignal !== 'undefined' && value instanceof AbortSignal;
}

function isRequestSignalSource(value: unknown): value is RequestSignalSource {
  return isAbortSignal(value)
    || (typeof value === 'object' && value !== null && 'signal' in value
      && isAbortSignal((value as { signal?: unknown }).signal));
}

export function createRequestSignal(
  sources: readonly RequestSignalSource[],
  timeoutMs = DEFAULT_REQUEST_TIMEOUT_MS
): RequestSignalHandle {
  const controller = new AbortController();
  const callers = [...new Set(sources.map(sourceSignal).filter((signal): signal is AbortSignal => Boolean(signal)))];
  let timedOut = false;
  let disposed = false;
  let timer: ReturnType<typeof setTimeout> | undefined;
  const removers: Array<() => void> = [];

  const cleanup = () => {
    if (timer !== undefined) {
      clearTimeout(timer);
      timer = undefined;
    }
    for (const remove of removers.splice(0)) remove();
  };

  controller.signal.addEventListener('abort', cleanup, { once: true });

  const abortFromCaller = (signal: AbortSignal) => {
    if (!disposed && !controller.signal.aborted) controller.abort(signal.reason);
  };
  const abortFromTimeout = () => {
    if (disposed || controller.signal.aborted) return;
    timedOut = true;
    const error = new Error('Request timed out.');
    error.name = 'TimeoutError';
    controller.abort(error);
  };

  for (const signal of callers) {
    if (controller.signal.aborted) break;
    if (signal.aborted) {
      abortFromCaller(signal);
      break;
    }
    const listener = () => abortFromCaller(signal);
    signal.addEventListener('abort', listener, { once: true });
    removers.push(() => signal.removeEventListener('abort', listener));
  }

  if (!controller.signal.aborted) timer = setTimeout(abortFromTimeout, timeoutMs);

  return {
    signal: controller.signal,
    timedOut: () => timedOut,
    dispose() {
      if (disposed) return;
      disposed = true;
      cleanup();
      controller.signal.removeEventListener('abort', cleanup);
    }
  };
}
