import { describe, expect, it } from 'vitest';
import { stableOccurrenceKeys } from './stableKeys';

describe('stableOccurrenceKeys', () => {
  it('keeps duplicate-content keys deterministic without array-index coupling', () => {
    expect(stableOccurrenceKeys(['same warning', 'same warning', 'other warning'])).toEqual([
      'same warning:1', 'same warning:2', 'other warning:1'
    ]);
    expect(stableOccurrenceKeys(['other warning', 'same warning', 'same warning'])).toEqual([
      'other warning:1', 'same warning:1', 'same warning:2'
    ]);
  });
});
