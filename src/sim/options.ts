/**
 * Parses a whole-number command-line option. Anything else (a typo such as `--workers foo`, a decimal, a number
 * below `min`) is an error that names the option, so it can never reach the worker pool or the seed range as NaN.
 * Returns undefined when the option was not given.
 */
export function parseInteger(name: string, raw: string | undefined, opts: { min?: number } = {}): number | undefined {
  if (raw === undefined) return undefined;
  const n = Number(raw);
  const min = opts.min ?? -Infinity;
  if (raw.trim() === '' || !Number.isInteger(n) || n < min) {
    throw new Error(`--${name} must be a whole number${Number.isFinite(min) ? ` of at least ${min}` : ''} (got "${raw}")`);
  }
  return n;
}
