import { describe, expect, it } from 'vitest';
import { buildLoginPath, validateReturnTarget } from './navigation';

describe('same-origin return targets', () => {
  it.each([
    '/projects/42?tab=delivery#evidence',
    '/settings?section=identity',
    '/organization/employees/42',
    '/?tab=overview#summary',
    '/#summary',
    '/?tab=overview'
  ])('preserves a safe relative target %s', target => {
    expect(validateReturnTarget(target)).toBe(target);
  });

  it.each([
    'https://evil.example/path',
    'http://regent.invalid/path',
    'javascript:alert(1)',
    '//evil.example/path',
    '///evil.example/path',
    '/\\evil.example/path',
    '/%5Cevil.example/path',
    '/%2F%2Fevil.example/path',
    '/projects\n/42',
    '/%09/projects',
    '/%',
    'projects/42',
    ' /projects',
    '/projects ',
    '/login',
    '/',
    '/login?returnTo=%2Fprojects',
    '/login/',
    '/%6Cogin/'
  ])('rejects an unsafe or looping target %s', target => {
    expect(validateReturnTarget(target)).toBeNull();
  });

  it('constructs an encoded login return target', () => {
    expect(buildLoginPath('/projects/42?tab=delivery#evidence'))
      .toBe('/login?returnTo=%2Fprojects%2F42%3Ftab%3Ddelivery%23evidence');
    expect(buildLoginPath('/?tab=overview#summary'))
      .toBe('/login?returnTo=%2F%3Ftab%3Doverview%23summary');
    expect(buildLoginPath('/')).toBe('/login');
    expect(buildLoginPath('//evil.example')).toBe('/login');
  });
});
