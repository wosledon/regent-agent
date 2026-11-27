import { test } from '@playwright/test';

// Playwright runs in Node, while the dashboard TypeScript project intentionally excludes Node globals.
const environment = (globalThis as typeof globalThis & {
  process?: { env: Record<string, string | undefined> };
}).process?.env ?? {};

export const externalStack = Boolean(environment.TENANT_URL && environment.OPS_URL);
export const tenantUrl = environment.TENANT_URL ?? 'http://localhost:5273';
export const opsUrl = environment.OPS_URL ?? 'http://localhost:4273';
export const externalPreviewUrl = environment.TENANT_PREVIEW_URL;
export const tenantPreviewUrl = externalPreviewUrl ?? 'http://localhost:5274';

export function requireDevelopmentAuth() {
  test.skip(externalStack, 'Requires Development-only demo authentication or Agent token issuance.');
}

export function skipMockedAuthInExternalStack() {
  test.skip(externalStack, 'Requires an authenticated live session; external-stack mode does not mock authentication.');
}
