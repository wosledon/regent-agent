import { afterEach, describe, expect, it, vi } from 'vitest';
import { sanitizeTelemetryPath, startBrowserTelemetry, vitalPassed } from './browserTelemetry';

let stopTelemetry: (() => void) | undefined;

afterEach(() => {
  stopTelemetry?.();
  stopTelemetry = undefined;
  vi.useRealTimers();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('browser telemetry privacy and thresholds', () => {
  it('removes query strings and high-cardinality identifiers', () => {
    expect(sanitizeTelemetryPath('/projects/123e4567-e89b-12d3-a456-426614174000?token=secret#private'))
      .toBe('/projects/:id');
    expect(sanitizeTelemetryPath('/employees/42/timeline?employee=42')).toBe('/employees/:id/timeline');
  });

  it('uses Core Web Vital thresholds without sending page content', () => {
    expect(vitalPassed('web.vital.lcp', 2500)).toBe(true);
    expect(vitalPassed('web.vital.lcp', 2501)).toBe(false);
    expect(vitalPassed('web.vital.cls', 0.1)).toBe(true);
    expect(vitalPassed('web.vital.inp', 201)).toBe(false);
  });

  it('does not throw from page or error handlers when UUID generation is unavailable', () => {
    vi.useFakeTimers();
    vi.stubGlobal('crypto', {});
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 202 }));
    vi.stubGlobal('fetch', fetchMock);
    stopTelemetry = startBrowserTelemetry({ endpoint: '/telemetry/browser', serviceName: 'test' });

    expect(() => window.dispatchEvent(new Event('error'))).not.toThrow();
    expect(() => window.dispatchEvent(new Event('pagehide'))).not.toThrow();
    expect(fetchMock).toHaveBeenCalledOnce();
  });

  it('does not throw when beacon and fallback transport both fail', () => {
    vi.useFakeTimers();
    vi.stubGlobal('crypto', { randomUUID: () => '123e4567-e89b-42d3-a456-426614174000' });
    Object.defineProperty(navigator, 'sendBeacon', { configurable: true, value: vi.fn(() => { throw new Error('beacon failed'); }) });
    vi.stubGlobal('fetch', vi.fn(() => { throw new Error('fetch failed'); }));
    stopTelemetry = startBrowserTelemetry({ endpoint: '/telemetry/browser', serviceName: 'test' });

    expect(() => window.dispatchEvent(new Event('error'))).not.toThrow();
    expect(() => window.dispatchEvent(new Event('pagehide'))).not.toThrow();
  });
});
