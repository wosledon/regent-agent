import { afterEach, describe, expect, it, vi } from 'vitest';
import { createUuidV4 } from './uuid';

const originalCrypto = globalThis.crypto;

afterEach(() => {
  Object.defineProperty(globalThis, 'crypto', { configurable: true, value: originalCrypto });
  vi.restoreAllMocks();
});

describe('createUuidV4', () => {
  it('prefers native randomUUID and returns lowercase RFC 4122 version 4', () => {
    const randomUUID = vi.fn(() => '123e4567-e89b-42d3-a456-426614174000');
    Object.defineProperty(globalThis, 'crypto', { configurable: true, value: { randomUUID } });
    expect(createUuidV4()).toBe('123e4567-e89b-42d3-a456-426614174000');
    expect(randomUUID).toHaveBeenCalledOnce();
  });

  it('uses getRandomValues with version and variant bits when randomUUID is absent', () => {
    const getRandomValues = vi.fn(<T extends ArrayBufferView | null>(array: T): T => {
      if (array instanceof Uint8Array) array.fill(0xff);
      return array;
    });
    Object.defineProperty(globalThis, 'crypto', { configurable: true, value: { getRandomValues } });
    const uuid = createUuidV4();
    expect(uuid).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
    expect(getRandomValues).toHaveBeenCalledOnce();
  });

  it('fails closed without a CSPRNG and never falls back to Math.random', () => {
    const random = vi.spyOn(Math, 'random').mockReturnValue(0.5);
    Object.defineProperty(globalThis, 'crypto', { configurable: true, value: {} });
    expect(() => createUuidV4()).toThrow(/cryptographically secure/);
    expect(random).not.toHaveBeenCalled();
  });
});
