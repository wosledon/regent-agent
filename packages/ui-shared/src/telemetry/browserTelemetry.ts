export interface BrowserTelemetrySample {
  metric: string;
  value: number;
  success: boolean;
  traceId?: string;
  detail?: string;
}

export interface BrowserTelemetryConfig {
  endpoint: string;
  serviceName: string;
  sampleRate?: number;
  release?: string;
}

let active: BrowserTelemetry | null = null;

export function startBrowserTelemetry(config: BrowserTelemetryConfig): () => void {
  active?.dispose();
  if (!config.endpoint || (config.sampleRate ?? 1) <= 0) return () => undefined;
  const telemetry = new BrowserTelemetry(config);
  active = telemetry;
  telemetry.start();
  return () => telemetry.dispose();
}

export function trackBrowserPageView(path: string) {
  active?.pageView(path);
}

export function trackBrowserApiOutcome(status: number, requestId?: string) {
  active?.track('api.request', status, status >= 200 && status < 400, requestId);
}

export function sanitizeTelemetryPath(path: string): string {
  const withoutQuery = path.split(/[?#]/, 1)[0] || '/';
  return withoutQuery
    .replace(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/gi, ':id')
    .replace(/\d+/g, ':id')
    .replace(/\/{2,}/g, '/')
    .slice(0, 160);
}

export function vitalPassed(metric: string, value: number): boolean {
  if (metric === 'web.vital.lcp') return value <= 2500;
  if (metric === 'web.vital.cls') return value <= 0.1;
  if (metric === 'web.vital.inp') return value <= 200;
  return true;
}

class BrowserTelemetry {
  private readonly queue: BrowserTelemetrySample[] = [];
  private readonly cleanups: Array<() => void> = [];
  private flushTimer: ReturnType<typeof setTimeout> | null = null;
  private disposed = false;

  constructor(private readonly config: BrowserTelemetryConfig) {}

  start() {
    this.listen(window, 'error', () => this.track('browser.error', 1, false));
    this.listen(window, 'unhandledrejection', () => this.track('browser.error', 1, false));
    this.observe('largest-contentful-paint', entry => this.track('web.vital.lcp', entry.startTime, vitalPassed('web.vital.lcp', entry.startTime)), true);
    this.observe('layout-shift', entry => {
      const shift = entry as PerformanceEntry & { value: number; hadRecentInput?: boolean };
      if (!shift.hadRecentInput) this.track('web.vital.cls', shift.value, vitalPassed('web.vital.cls', shift.value));
    });
    this.observe('event', entry => this.track('web.vital.inp', entry.duration, vitalPassed('web.vital.inp', entry.duration)), true);
    this.listen(window, 'pagehide', () => this.flush());
  }

  pageView(path: string) {
    this.track('web.page_view', performance.now(), true, undefined, sanitizeTelemetryPath(path));
  }

  track(metric: string, value: number, success: boolean, traceId?: string, detail?: string) {
    if (this.disposed || Math.random() > (this.config.sampleRate ?? 1) || !Number.isFinite(value)) return;
    if (!/^[a-z0-9_.:-]{1,128}$/i.test(metric)) return;
    this.queue.push({ metric, value, success, traceId: traceId ?? crypto.randomUUID(), ...(detail ? { detail } : {}) });
    if (this.queue.length > 50) this.queue.splice(0, this.queue.length - 50);
    if (!this.flushTimer) this.flushTimer = setTimeout(() => this.flush(), 2000);
  }

  flush() {
    if (this.flushTimer) clearTimeout(this.flushTimer);
    this.flushTimer = null;
    if (this.disposed || this.queue.length === 0) return;
    const payload = JSON.stringify({
      serviceName: this.config.serviceName,
      release: this.config.release,
      samples: this.queue.splice(0, 50)
    });
    if (navigator.sendBeacon) navigator.sendBeacon(this.config.endpoint, new Blob([payload], { type: 'application/json' }));
    else void fetch(this.config.endpoint, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: payload, keepalive: true }).catch(() => undefined);
  }

  dispose() {
    this.disposed = true;
    this.flush();
    this.cleanups.splice(0).forEach(cleanup => cleanup());
    if (active === this) active = null;
  }

  private listen(target: Window, name: string, handler: EventListener) {
    target.addEventListener(name, handler);
    this.cleanups.push(() => target.removeEventListener(name, handler));
  }

  private observe(type: string, callback: (entry: PerformanceEntry) => void, buffered = false) {
    if (typeof PerformanceObserver === 'undefined') return;
    try {
      const observer = new PerformanceObserver(list => list.getEntries().forEach(callback));
      observer.observe({ type, buffered, ...(type === 'event' ? { durationThreshold: 40 } : {}) } as PerformanceObserverInit);
      this.cleanups.push(() => observer.disconnect());
    } catch { /* Unsupported web vital in this browser. */ }
  }
}
