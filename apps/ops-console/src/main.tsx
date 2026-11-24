import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AppThemeProvider, startBrowserTelemetry } from '@regent/ui-shared';
import { App } from './App';

startBrowserTelemetry({
  endpoint: import.meta.env.VITE_TELEMETRY_ENDPOINT || '/telemetry/browser',
  serviceName: import.meta.env.VITE_TELEMETRY_SERVICE || 'ops-console',
  sampleRate: Number(import.meta.env.VITE_TELEMETRY_SAMPLE_RATE ?? 1),
  release: import.meta.env.VITE_RELEASE
});

const queryClient = new QueryClient();

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <AppThemeProvider><App /></AppThemeProvider>
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>
);
