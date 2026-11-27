import { useMemo, type PropsWithChildren } from 'react';
import { CssBaseline, ThemeProvider } from '@mui/material';
import { createAppTheme, type AppKind } from './theme';

export interface AppThemeProviderProps extends PropsWithChildren {
  appKind?: AppKind;
}

export function AppThemeProvider({ children, appKind = 'tenant' }: AppThemeProviderProps) {
  const theme = useMemo(() => createAppTheme(appKind), [appKind]);
  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      {children}
    </ThemeProvider>
  );
}
