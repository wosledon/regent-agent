import { createTheme, type ThemeOptions } from '@mui/material/styles';

export const designTokens = {
  color: {
    primary: '#3157D5',
    primaryHover: '#2443A6',
    primarySoft: '#E9EEFF',
    accent: '#6D5BD0',
    accentText: '#4F3DA3',
    accentSoft: '#F2EFFF',
    success: '#157347',
    successSoft: '#EAF7F0',
    warning: '#9A5B00',
    warningSoft: '#FFF4D6',
    error: '#B42318',
    errorSoft: '#FEF3F2',
    info: '#175CD3',
    infoSoft: '#EFF8FF',
    background: '#F4F6FA',
    canvas: '#F8FAFC',
    surface: '#FFFFFF',
    surfaceHover: '#EEF2F7',
    surfaceSelected: '#EDF2FF',
    textPrimary: '#172033',
    textSecondary: '#4B5568',
    textTertiary: '#5F6B7F',
    textDisabled: '#98A2B3',
    borderSubtle: '#E8ECF2',
    borderDefault: '#D8DEE9',
    borderStrong: '#B8C2D3',
    borderControl: '#7F8A9E',
    navBackground: '#111A2E',
    navSurface: '#17223A',
    navText: '#F5F7FC',
    navTextMuted: '#A9B6CF',
    navBorder: '#2A3854',
    navActive: '#22345A',
    navIndicator: '#7FA2FF'
  },
  font: {
    sans: '-apple-system, BlinkMacSystemFont, "Segoe UI", "Noto Sans SC", "PingFang SC", "Microsoft YaHei", sans-serif',
    mono: '"SFMono-Regular", Consolas, "Liberation Mono", monospace'
  },
  radius: { xs: 4, sm: 6, md: 8, lg: 12, pill: 999 },
  spacing: { xxs: 2, xs: 4, sm: 8, md: 12, lg: 16, xl: 20, xxl: 24, xxxl: 32 },
  layout: {
    sidebarWidth: 248,
    topbarHeight: 56,
    contentMaxWidth: 1440,
    wideContentMaxWidth: 1600,
    readingMaxWidth: 880
  },
  motion: {
    fast: '100ms',
    normal: '160ms',
    slow: '220ms',
    easing: 'cubic-bezier(.2,0,0,1)'
  }
} as const;

export type AppKind = 'tenant' | 'ops';

const shadows: NonNullable<ThemeOptions['shadows']> = [
  'none',
  '0 1px 2px rgba(16,24,40,.06), 0 1px 3px rgba(16,24,40,.10)',
  '0 12px 32px rgba(16,24,40,.18)',
  '0 16px 48px rgba(16,24,40,.24)',
  '0 20px 56px rgba(16,24,40,.28)',
  '0 24px 64px rgba(16,24,40,.32)',
  '0 32px 80px rgba(16,24,40,.36)',
  '0 40px 96px rgba(16,24,40,.40)',
  '0 48px 112px rgba(16,24,40,.44)',
  '0 56px 128px rgba(16,24,40,.48)',
  '0 64px 144px rgba(16,24,40,.52)',
  '0 72px 160px rgba(16,24,40,.56)',
  '0 80px 176px rgba(16,24,40,.60)',
  '0 88px 192px rgba(16,24,40,.64)',
  '0 96px 208px rgba(16,24,40,.68)',
  '0 104px 224px rgba(16,24,40,.72)',
  '0 112px 240px rgba(16,24,40,.76)',
  '0 120px 256px rgba(16,24,40,.80)',
  '0 128px 272px rgba(16,24,40,.84)',
  '0 136px 288px rgba(16,24,40,.88)',
  '0 144px 304px rgba(16,24,40,.92)',
  '0 152px 320px rgba(16,24,40,.96)',
  '0 160px 336px rgba(16,24,40,1)',
  '0 168px 352px rgba(16,24,40,1)',
  '0 176px 368px rgba(16,24,40,1)'
];

export function createAppTheme(appKind: AppKind = 'tenant') {
  const controlHeight = appKind === 'ops' ? 34 : 36;
  const options: ThemeOptions = {
    palette: {
      mode: 'light',
      primary: { main: designTokens.color.primary, dark: designTokens.color.primaryHover, light: designTokens.color.primarySoft, contrastText: '#FFFFFF' },
      secondary: { main: designTokens.color.accent, dark: designTokens.color.accentText, light: designTokens.color.accentSoft, contrastText: '#FFFFFF' },
      success: { main: designTokens.color.success, light: designTokens.color.successSoft, contrastText: '#FFFFFF' },
      warning: { main: designTokens.color.warning, light: designTokens.color.warningSoft, contrastText: '#FFFFFF' },
      error: { main: designTokens.color.error, light: designTokens.color.errorSoft, contrastText: '#FFFFFF' },
      info: { main: designTokens.color.info, light: designTokens.color.infoSoft, contrastText: '#FFFFFF' },
      background: { default: designTokens.color.background, paper: designTokens.color.surface },
      text: { primary: designTokens.color.textPrimary, secondary: designTokens.color.textSecondary, disabled: designTokens.color.textDisabled },
      divider: designTokens.color.borderSubtle,
      action: {
        hover: designTokens.color.surfaceHover,
        selected: designTokens.color.surfaceSelected,
        disabled: designTokens.color.textDisabled,
        disabledBackground: '#F2F4F7',
        focus: 'rgba(49,87,213,.16)'
      }
    },
    shape: { borderRadius: designTokens.radius.md },
    shadows,
    typography: {
      fontFamily: designTokens.font.sans,
      fontSize: 14,
      fontWeightLight: 400,
      fontWeightRegular: 400,
      fontWeightMedium: 600,
      fontWeightBold: 700,
      h1: { fontSize: '1.75rem', lineHeight: '2.25rem', fontWeight: 600, letterSpacing: '-0.02em', overflowWrap: 'anywhere' },
      h2: { fontSize: '1.25rem', lineHeight: '1.75rem', fontWeight: 600, letterSpacing: '-0.01em', overflowWrap: 'anywhere' },
      h3: { fontSize: '.9375rem', lineHeight: '1.375rem', fontWeight: 600, letterSpacing: '-0.01em', overflowWrap: 'anywhere' },
      h4: { fontSize: '.875rem', lineHeight: '1.375rem', fontWeight: 600, overflowWrap: 'anywhere' },
      h5: { fontSize: '.8125rem', lineHeight: '1.25rem', fontWeight: 600, overflowWrap: 'anywhere' },
      h6: { fontSize: '.75rem', lineHeight: '1rem', fontWeight: 600, letterSpacing: '.04em', textTransform: 'uppercase', overflowWrap: 'anywhere' },
      subtitle1: { fontSize: '.875rem', lineHeight: '1.375rem', fontWeight: 400 },
      subtitle2: { fontSize: '.8125rem', lineHeight: '1.25rem', fontWeight: 600 },
      body1: { fontSize: '.875rem', lineHeight: '1.375rem' },
      body2: { fontSize: '.8125rem', lineHeight: '1.25rem' },
      button: { fontSize: '.8125rem', lineHeight: '1.25rem', fontWeight: 600, textTransform: 'none' },
      caption: { fontSize: '.75rem', lineHeight: '1rem', fontWeight: 500 },
      overline: { fontSize: '.6875rem', lineHeight: '.875rem', fontWeight: 600, letterSpacing: '.08em', textTransform: 'uppercase' }
    },
    components: {
      MuiCssBaseline: {
        styleOverrides: {
          body: {
            backgroundColor: designTokens.color.background,
            color: designTokens.color.textPrimary,
            textRendering: 'optimizeLegibility',
            WebkitFontSmoothing: 'antialiased',
            MozOsxFontSmoothing: 'grayscale'
          },
          '#root': { minHeight: '100vh' },
          '::selection': { backgroundColor: designTokens.color.primarySoft, color: designTokens.color.textPrimary },
          '@media (prefers-reduced-motion: reduce)': {
            '*, *::before, *::after': { animationDuration: '0ms !important', transitionDuration: '0ms !important', scrollBehavior: 'auto !important' }
          }
        }
      },
      MuiAppBar: {
        defaultProps: { elevation: 0, color: 'inherit' },
        styleOverrides: { root: { backgroundColor: designTokens.color.surface, color: designTokens.color.textPrimary, borderBottom: `1px solid ${designTokens.color.borderSubtle}`, boxShadow: 'none' } }
      },
      MuiToolbar: {
        styleOverrides: { root: { minHeight: `${designTokens.layout.topbarHeight}px !important`, minWidth: 0 } }
      },
      MuiButton: {
        defaultProps: { disableElevation: true },
        styleOverrides: {
          root: { minHeight: controlHeight, padding: '6px 14px', borderRadius: designTokens.radius.sm, fontSize: '.8125rem', fontWeight: 600, boxShadow: 'none', transition: `background-color ${designTokens.motion.fast} ${designTokens.motion.easing}, border-color ${designTokens.motion.fast} ${designTokens.motion.easing}` },
          contained: { boxShadow: 'none', '&:hover': { boxShadow: 'none' } },
          outlined: { borderColor: designTokens.color.borderDefault, backgroundColor: designTokens.color.surface, '&:hover': { borderColor: designTokens.color.borderStrong, backgroundColor: designTokens.color.surfaceHover } },
          text: { '&:hover': { backgroundColor: designTokens.color.surfaceHover } }
        }
      },
      MuiIconButton: {
        styleOverrides: { root: { borderRadius: designTokens.radius.sm, width: 36, height: 36, '&:hover': { backgroundColor: designTokens.color.surfaceHover } } }
      },
      MuiCard: {
        defaultProps: { variant: 'outlined' },
        styleOverrides: { root: { borderRadius: designTokens.radius.md, borderColor: designTokens.color.borderDefault, backgroundImage: 'none', boxShadow: 'none' } }
      },
      MuiPaper: {
        styleOverrides: { root: { backgroundImage: 'none' }, outlined: { borderColor: designTokens.color.borderDefault } }
      },
      MuiOutlinedInput: {
        styleOverrides: {
          root: { minHeight: controlHeight, borderRadius: designTokens.radius.sm, backgroundColor: designTokens.color.surface, fontSize: '.875rem', '&:hover .MuiOutlinedInput-notchedOutline': { borderColor: designTokens.color.borderStrong }, '&.Mui-focused .MuiOutlinedInput-notchedOutline': { borderColor: designTokens.color.primary, borderWidth: 2 }, '&.Mui-error .MuiOutlinedInput-notchedOutline': { borderColor: designTokens.color.error } },
          notchedOutline: { borderColor: designTokens.color.borderDefault },
          input: { padding: '7px 12px', '&::placeholder': { color: designTokens.color.textDisabled, opacity: 1 } }
        }
      },
      MuiInputLabel: {
        styleOverrides: { root: { fontSize: '.8125rem', fontWeight: 500, color: designTokens.color.textSecondary, '&.Mui-focused': { color: designTokens.color.primary } } }
      },
      MuiFormHelperText: {
        styleOverrides: { root: { fontSize: '.75rem', lineHeight: 1, marginLeft: 0, color: designTokens.color.textTertiary } }
      },
      MuiSelect: {
        defaultProps: { size: 'small' },
        styleOverrides: { root: { borderRadius: designTokens.radius.sm } }
      },
      MuiTableCell: {
        styleOverrides: {
          root: { fontSize: '.8125rem', lineHeight: 1.25, padding: '10px 12px', borderBottomColor: designTokens.color.borderSubtle, color: designTokens.color.textPrimary, fontVariantNumeric: 'tabular-nums' },
          head: { fontSize: '.75rem', lineHeight: 1, fontWeight: 600, color: designTokens.color.textSecondary, backgroundColor: designTokens.color.canvas, height: 40, whiteSpace: 'nowrap' },
          sizeSmall: { padding: '8px 12px' },
          stickyHeader: { backgroundColor: designTokens.color.canvas, zIndex: 2 }
        }
      },
      MuiTableRow: {
        styleOverrides: { root: { '&:last-of-type td': { borderBottom: 0 }, '&.MuiTableRow-hover:hover': { backgroundColor: designTokens.color.surfaceHover } }, head: { '&:hover': { backgroundColor: designTokens.color.canvas } } }
      },
      MuiChip: {
        styleOverrides: { root: { borderRadius: designTokens.radius.xs, fontSize: '.75rem', lineHeight: 1, fontWeight: 600, height: 24 }, outlined: { borderColor: designTokens.color.borderDefault }, filled: { boxShadow: 'none' } }
      },
      MuiAlert: {
        styleOverrides: { root: { borderRadius: designTokens.radius.md, fontSize: '.8125rem', lineHeight: 1.5, border: '1px solid' }, standardError: { backgroundColor: designTokens.color.errorSoft, borderColor: '#FECDCA', color: designTokens.color.error }, standardWarning: { backgroundColor: designTokens.color.warningSoft, borderColor: '#E7C46A', color: designTokens.color.warning }, standardSuccess: { backgroundColor: designTokens.color.successSoft, borderColor: '#B7E1C8', color: designTokens.color.success }, standardInfo: { backgroundColor: designTokens.color.infoSoft, borderColor: '#B2DDFF', color: designTokens.color.info } }
      },
      MuiDialog: {
        styleOverrides: { paper: { borderRadius: designTokens.radius.lg, border: `1px solid ${designTokens.color.borderDefault}`, boxShadow: shadows[2], backgroundImage: 'none' } }
      },
      MuiDrawer: {
        styleOverrides: { paper: { borderRight: `1px solid ${designTokens.color.borderSubtle}`, backgroundImage: 'none' } }
      },
      MuiListItemButton: {
        styleOverrides: { root: { minHeight: 36, borderRadius: designTokens.radius.sm, paddingTop: 6, paddingBottom: 6, '&.Mui-selected': { backgroundColor: designTokens.color.primarySoft }, '&.Mui-selected:hover': { backgroundColor: designTokens.color.primarySoft } } }
      },
      MuiListItemText: {
        styleOverrides: { root: { margin: 0 }, primary: { fontSize: '.8125rem', lineHeight: 1.25, fontWeight: 500 } }
      },
      MuiDivider: { styleOverrides: { root: { borderColor: designTokens.color.borderSubtle } } },
      MuiLink: { styleOverrides: { root: { color: designTokens.color.primary, textDecoration: 'none', '&:hover': { textDecoration: 'underline' } } } },
      MuiTooltip: { defaultProps: { arrow: true, enterDelay: 250 }, styleOverrides: { tooltip: { backgroundColor: designTokens.color.navBackground, fontSize: '.75rem', borderRadius: designTokens.radius.xs, padding: '6px 8px' }, arrow: { color: designTokens.color.navBackground } } },
      MuiSkeleton: { defaultProps: { animation: 'pulse' }, styleOverrides: { root: { backgroundColor: '#E9EDF3', borderRadius: designTokens.radius.xs } } }
    },
    breakpoints: { values: { xs: 0, sm: 600, md: 900, lg: 1200, xl: 1536 } }
  };
  return createTheme(options);
}

export const appTheme = createAppTheme('tenant');
