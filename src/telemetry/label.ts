import type { FightRecord } from './record';

/** The longest tester ID kept; it goes into folder names and report rows. */
export const MAX_TESTER_LENGTH = 20;

/**
 * A tester ID made safe for a folder name and a column: letters, digits, "_" and "-" only, at most MAX_TESTER_LENGTH
 * characters. Anything else becomes "_". Returns undefined for an empty or non-text value, meaning "no tester".
 */
export function cleanTester(raw: unknown): string | undefined {
  if (typeof raw !== 'string') return undefined;
  const t = raw.trim().replace(/[^\w-]/g, '_').slice(0, MAX_TESTER_LENGTH);
  return t === '' ? undefined : t;
}

/**
 * The folder name for one fight's telemetry: `<time>-<preset>-<outcome>`, with the tester and attempt number after
 * the time when the fight has a tester (`<time>-P3-a2-<preset>-<outcome>`), so a round's folders sort by time and
 * group by tester at a glance.
 */
export function telemetryFolderName(time: Date, meta: FightRecord['meta'], outcome: string): string {
  const stamp = time.toISOString().replace(/[:.]/g, '-');
  const tester = cleanTester(meta?.tester);
  const who = tester ? `-${tester}${meta?.attempt !== undefined ? `-a${meta.attempt}` : ''}` : '';
  return `${stamp}${who}-${meta?.preset ?? 'custom'}-${outcome}`;
}
