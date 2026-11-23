import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  use: { ...devices['Desktop Chrome'], baseURL: 'http://localhost:5173' },
  webServer: [
    {
      command: 'corepack pnpm --filter @regent/tenant-dashboard dev',
      url: 'http://localhost:5173',
      reuseExistingServer: true,
      timeout: 120_000
    },
    {
      command: 'corepack pnpm --filter @regent/ops-console dev',
      url: 'http://localhost:4173',
      reuseExistingServer: true,
      timeout: 120_000
    }
  ]
});
