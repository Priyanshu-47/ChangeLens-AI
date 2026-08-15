import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach } from 'vitest';

// jsdom does not implement scrollIntoView; the evidence-linking handler calls it.
// Stub it so clicking an evidence chip in tests does not emit "not implemented".
Element.prototype.scrollIntoView = () => {};

// jsdom 25 ships crypto.randomUUID via its webcrypto implementation, but guard
// anyway so tests never depend on the host runtime's crypto surface.
if (typeof crypto === 'undefined' || typeof crypto.randomUUID !== 'function') {
  // @ts-expect-error test-only fallback
  globalThis.crypto = {
    randomUUID: () => '00000000-0000-4000-8000-000000000000',
  };
}

afterEach(() => {
  cleanup();
  window.localStorage.clear();
  vi.unstubAllGlobals();
});
