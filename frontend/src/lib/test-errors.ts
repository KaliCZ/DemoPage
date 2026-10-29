// Throws from minified bundle code to verify Sentry source maps resolve; the nested call gives the stack a second in-app frame.
export function crashFromBundledModule(): never {
  formatAndThrow("a bundled module");
}

function formatAndThrow(source: string): never {
  throw new Error(`Test client-side error thrown from ${source}.`);
}
