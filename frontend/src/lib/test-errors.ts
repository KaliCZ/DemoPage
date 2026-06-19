// Throws from bundled (Vite-minified) code so the Sentry source-map upload can be
// verified end to end: without uploaded maps the resulting issue points at a minified
// `_astro/*.js` frame; with maps it resolves back to this file. The nested call gives
// the stack more than one in-app frame to resolve. Wired from the admin test-errors page.
export function crashFromBundledModule(): never {
  formatAndThrow("a bundled module");
}

function formatAndThrow(source: string): never {
  throw new Error(`Test client-side error thrown from ${source}.`);
}
