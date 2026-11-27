import { defineConfig, devices } from '@playwright/test';
import { externalStack, tenantUrl } from './e2e/support/externalStack';

export default defineConfig({
  testDir: './e2e',
  use: { ...devices['Desktop Chrome'], baseURL: externalStack ? tenantUrl : 'http://localhost:5273' },
  webServer: externalStack ? undefined : [
    {
      command: 'corepack pnpm --filter @regent/tenant-dashboard exec vite --host 127.0.0.1 --port 5273 --strictPort',
      url: 'http://localhost:5273',
      reuseExistingServer: true,
      timeout: 120_000
    },
    {
      command: 'corepack pnpm --filter @regent/ops-console exec vite --host 127.0.0.1 --port 4273 --strictPort',
      url: 'http://localhost:4273',
      reuseExistingServer: true,
      timeout: 120_000
    },
    {
      command: 'corepack pnpm --filter @regent/tenant-dashboard build && corepack pnpm --filter @regent/tenant-dashboard exec vite preview --host 127.0.0.1 --port 5274 --strictPort',
      url: 'http://localhost:5274',
      reuseExistingServer: true,
      timeout: 120_000
    }
  ]
});
