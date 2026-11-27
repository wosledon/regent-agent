import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  build: {
    rollupOptions: {
      output: {
        manualChunks: {
          react: ['react', 'react-dom', 'react-router-dom'],
          mui: ['@mui/material', '@mui/icons-material', '@emotion/react', '@emotion/styled'],
          query: ['@tanstack/react-query'],
          signalr: ['@microsoft/signalr']
        }
      }
    }
  },
  server: {
    port: 5173,
    proxy: {
      '/v1': 'http://localhost:5080',
      '/agent/v1': 'http://localhost:5080',
      '/auth': 'http://localhost:5080',
      '/telemetry': 'http://localhost:5082',
      '/hubs': { target: 'http://localhost:5080', ws: true },
      '/openapi': 'http://localhost:5080'
    }
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    exclude: ['e2e/**', 'node_modules/**'],
    testTimeout: 15_000
  }
});
