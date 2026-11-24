import { describe, expect, it } from 'vitest';
import { sanitizeTelemetryPath, vitalPassed } from './browserTelemetry';

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
});
