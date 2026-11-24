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
          query: ['@tanstack/react-query']
        }
      }
    }
  },
  server: {
    port: 4173,
    proxy: {
      '/ops': 'http://localhost:5082', '/auth': 'http://localhost:5082', '/telemetry': 'http://localhost:5082',
      '/health': 'http://localhost:5082', '/public-status': 'http://localhost:5082'
    }
  },
  test: { environment: 'jsdom', setupFiles: ['./src/test/setup.ts'] }
});
