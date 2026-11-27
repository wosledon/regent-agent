import './lib/i18n';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { BrowserRouter } from 'react-router-dom';
import { AppThemeProvider, startBrowserTelemetry } from '@regent/ui-shared';
import { App } from './app/App';

startBrowserTelemetry({
  endpoint: import.meta.env.VITE_TELEMETRY_ENDPOINT || '/telemetry/browser',
  serviceName: import.meta.env.VITE_TELEMETRY_SERVICE || 'tenant-dashboard',
  sampleRate: Number(import.meta.env.VITE_TELEMETRY_SAMPLE_RATE ?? 1),
  release: import.meta.env.VITE_RELEASE
});

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { staleTime: 15_000, retry: 2, refetchOnWindowFocus: false }
  }
});

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <AppThemeProvider>
          <App />
        </AppThemeProvider>
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>
);
