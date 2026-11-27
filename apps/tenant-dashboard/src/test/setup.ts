import '@testing-library/jest-dom/vitest';
import i18n from '../lib/i18n';
import { cleanup } from '@testing-library/react';
import { afterEach, beforeEach } from 'vitest';

beforeEach(async () => {
  await i18n.changeLanguage('en-US');
});

afterEach(cleanup);
