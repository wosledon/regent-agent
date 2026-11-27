const VALIDATION_ORIGIN = 'https://regent.invalid';

function hasControlCharacters(value: string): boolean {
  return Array.from(value).some(character => {
    const code = character.charCodeAt(0);
    return code <= 0x1f || code === 0x7f;
  });
}

function decoded(value: string): string | null {
  try {
    return decodeURIComponent(value);
  } catch {
    return null;
  }
}

export function validateReturnTarget(value: string | null | undefined, loginPath = '/login'): string | null {
  if (!value || value !== value.trim() || !value.startsWith('/') || value.startsWith('//')) return null;
  if (value.includes('\\') || hasControlCharacters(value)) return null;

  const decodedValue = decoded(value);
  if (decodedValue === null || decodedValue.startsWith('//') || decodedValue.includes('\\') || hasControlCharacters(decodedValue)) return null;

  let parsed: URL;
  try {
    parsed = new URL(value, VALIDATION_ORIGIN);
  } catch {
    return null;
  }
  if (parsed.origin !== VALIDATION_ORIGIN) return null;

  const pathname = decoded(parsed.pathname);
  if (pathname === null) return null;
  const normalizedPath = pathname.replace(/\/+$/, '') || '/';
  const normalizedLoginPath = loginPath.replace(/\/+$/, '') || '/';
  return normalizedPath === normalizedLoginPath || value === '/' ? null : value;
}

export function buildLoginPath(value?: string | null, loginPath = '/login'): string {
  const target = validateReturnTarget(value, loginPath);
  return target ? `${loginPath}?returnTo=${encodeURIComponent(target)}` : loginPath;
}
