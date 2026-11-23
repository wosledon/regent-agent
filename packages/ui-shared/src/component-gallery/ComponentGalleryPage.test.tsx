import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { AppThemeProvider } from '../theme/AppThemeProvider';
import { ComponentGalleryPage } from './ComponentGalleryPage';

describe('ComponentGalleryPage', () => {
  it('renders every registered component', () => {
    render(<AppThemeProvider><ComponentGalleryPage /></AppThemeProvider>);
    expect(screen.getByText('Component gallery')).toBeInTheDocument();
    expect(screen.getAllByText('Button').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Select').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Tooltip').length).toBeGreaterThan(0);
  });
});
