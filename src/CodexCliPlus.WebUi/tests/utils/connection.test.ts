import { describe, expect, it } from 'vitest';
import { computeApiUrl, isLocalhost, normalizeApiBase } from '../../src/utils/connection';

describe('connection utilities', () => {
  it('normalizes bare host values to HTTP base URLs', () => {
    expect(normalizeApiBase('localhost:12345/')).toBe('http://localhost:12345');
  });

  it('removes the management API suffix before recomputing it', () => {
    expect(computeApiUrl('http://localhost:3210/v0/management/')).toBe(
      'http://localhost:3210/v0/management'
    );
  });

  it('recognizes local hostnames used by the desktop shell', () => {
    expect(isLocalhost('localhost')).toBe(true);
    expect(isLocalhost('127.0.0.1')).toBe(true);
    expect(isLocalhost('example.com')).toBe(false);
  });
});
