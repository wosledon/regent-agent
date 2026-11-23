import { createTheme } from '@mui/material/styles';

export const designTokens = {
  color: {
    primary: '#3157D5',
    secondary: '#6D5BD0',
    success: '#2E7D32',
    warning: '#B26A00',
    error: '#C62828',
    info: '#0277BD',
    background: '#F5F7FB',
    surface: '#FFFFFF'
  },
  radius: { sm: 6, md: 10, lg: 16 },
  spacing: { xs: 4, sm: 8, md: 16, lg: 24, xl: 32 }
} as const;

export const appTheme = createTheme({
  palette: {
    mode: 'light',
    primary: { main: designTokens.color.primary },
    secondary: { main: designTokens.color.secondary },
    success: { main: designTokens.color.success },
    warning: { main: designTokens.color.warning },
    error: { main: designTokens.color.error },
    info: { main: designTokens.color.info },
    background: { default: designTokens.color.background, paper: designTokens.color.surface }
  },
  shape: { borderRadius: designTokens.radius.md },
  typography: {
    fontFamily: 'Inter, "Noto Sans SC", "Microsoft YaHei", sans-serif',
    button: { textTransform: 'none', fontWeight: 600 }
  },
  components: {
    MuiButton: { defaultProps: { disableElevation: true } },
    MuiCard: { defaultProps: { variant: 'outlined' } },
    MuiTextField: { defaultProps: { size: 'small', fullWidth: true } }
  }
});
