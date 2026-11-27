import { useId, type PropsWithChildren } from 'react';
import { Box, Paper, Stack } from '@mui/material';
import type { AppLocale } from '../locale';
import { designTokens } from '../theme/theme';
import { Icon, type IconName } from './Icon';
import { LocaleSwitch } from './LocaleSwitch';
import { Text } from './Text';

export interface AuthHighlight {
  icon: IconName;
  title: string;
  description: string;
}

export interface AuthLayoutProps extends PropsWithChildren {
  audience: string;
  headline: string;
  description: string;
  highlights: ReadonlyArray<AuthHighlight>;
  footer: string;
  locale: AppLocale;
  onLocaleChange: (locale: AppLocale) => void;
  localeLabel: string;
}

export function AuthLayout({ audience, headline, description, highlights, footer, locale,
  onLocaleChange, localeLabel, children }: AuthLayoutProps) {
  const marketingTitleId = useId();
  return (
    <Box sx={{
      minHeight: '100vh',
      display: 'grid',
      gridTemplateColumns: { xs: '1fr', md: 'minmax(0, 1.08fr) minmax(420px, 0.92fr)' },
      bgcolor: 'background.default'
    }}>
      <Box component="aside" aria-labelledby={marketingTitleId} data-testid="auth-marketing" sx={{
        display: { xs: 'none', md: 'flex' },
        position: 'relative',
        overflow: 'hidden',
        p: { md: 6, lg: 8 },
        color: designTokens.color.navText,
        bgcolor: designTokens.color.navBackground,
        '&::before': {
          content: '""',
          position: 'absolute',
          inset: 0,
          opacity: 0.5,
          backgroundImage: `linear-gradient(${designTokens.color.navBorder} 1px, transparent 1px), linear-gradient(90deg, ${designTokens.color.navBorder} 1px, transparent 1px)`,
          backgroundSize: '48px 48px',
          maskImage: 'linear-gradient(to bottom right, black, transparent 72%)'
        },
        '&::after': {
          content: '""',
          position: 'absolute',
          width: 280,
          height: 280,
          right: -120,
          bottom: -120,
          borderRadius: '50%',
          border: `1px solid ${designTokens.color.navIndicator}`,
          opacity: 0.35
        }
      }}>
        <Stack spacing={5} sx={{ position: 'relative', zIndex: 1, maxWidth: 620, width: '100%' }}>
          <Stack direction="row" spacing={1.5} alignItems="center">
            <Box sx={{ width: 40, height: 40, borderRadius: 1.5, display: 'grid', placeItems: 'center', bgcolor: designTokens.color.primary }}>
              <Icon name="agent" sx={{ color: 'common.white' }} />
            </Box>
            <Text component="span" sx={{ color: 'common.white', fontSize: '1rem', fontWeight: 700 }}>Regent Agent</Text>
          </Stack>
          <Stack spacing={2}>
            <Text variant="overline" sx={{ color: designTokens.color.navIndicator }}>{audience}</Text>
            <Text id={marketingTitleId} variant="display" component="h1" sx={{ color: 'common.white', maxWidth: '12ch' }}>{headline}</Text>
            <Text sx={{ fontSize: '1rem', color: designTokens.color.navTextMuted, maxWidth: '54ch' }}>{description}</Text>
          </Stack>
          <Stack spacing={0} sx={{ maxWidth: 560 }}>
            {highlights.map((item, index) => (
              <Stack key={item.title} direction="row" spacing={2} alignItems="flex-start" sx={{ py: 2, borderTop: `1px solid ${designTokens.color.navBorder}` }}>
                <Box sx={{ width: 34, height: 34, flexShrink: 0, borderRadius: 1, display: 'grid', placeItems: 'center', bgcolor: designTokens.color.navSurface, color: designTokens.color.navIndicator }}>
                  <Icon name={item.icon} sx={{ fontSize: 18 }} />
                </Box>
                <Stack spacing={0.25}>
                  <Text sx={{ fontWeight: 600, color: 'common.white' }}>{item.title}</Text>
                  <Text variant="secondary" sx={{ color: designTokens.color.navTextMuted }}>{item.description}</Text>
                </Stack>
                <Text variant="mono" sx={{ ml: 'auto', color: designTokens.color.navTextMuted }}>{String(index + 1).padStart(2, '0')}</Text>
              </Stack>
            ))}
          </Stack>
        </Stack>
      </Box>

      <Box sx={{ display: 'flex', flexDirection: 'column', minWidth: 0, minHeight: '100vh', p: { xs: 2, sm: 4 } }}>
        <Box component="header">
          <Stack direction="row" alignItems="center" spacing={2} sx={{ justifyContent: 'space-between', flexWrap: 'wrap' }}>
            <Box data-testid="auth-mobile-brand" sx={{ display: { md: 'none' } }}>
              <Stack direction="row" spacing={1} alignItems="center">
                <Box sx={{ width: 32, height: 32, borderRadius: 1, display: 'grid', placeItems: 'center', bgcolor: designTokens.color.navBackground }}>
                  <Icon name="agent" sx={{ color: 'common.white', fontSize: 18 }} />
                </Box>
                <Text component="span" sx={{ fontWeight: 700 }}>Regent Agent</Text>
              </Stack>
            </Box>
            <LocaleSwitch value={locale} onChange={onLocaleChange} label={localeLabel} />
          </Stack>
        </Box>
        <Box component="main" sx={{ flex: 1, display: 'grid', placeItems: 'center', py: { xs: 4, md: 6 } }}>
          <Paper elevation={0} variant="outlined" sx={{ width: '100%', maxWidth: 480, p: { xs: 3, sm: 4 }, borderRadius: 3, bgcolor: 'background.paper' }}>
            {children}
          </Paper>
        </Box>
        <Text component="footer" variant="caption" color="text.secondary" align="center">{footer}</Text>
      </Box>
    </Box>
  );
}
