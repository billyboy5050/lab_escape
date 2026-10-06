import type { Content } from '../content/types';
import type { Loadout } from '../state/types';
import { cleanTester, telemetryFolderName } from '../telemetry/label';
import { eventsToJsonl } from '../telemetry/record';
import type { FightSession } from '../telemetry/session';

export function telemetryFiles(session: FightSession): Record<string, string> {
  return {
    'replay.json': JSON.stringify(session.record, null, 2),
    'summary.json': JSON.stringify(session.summary(), null, 2),
    'events.jsonl': eventsToJsonl(session.events),
  };
}

/**
 * Saves a finished fight's replay, summary and event stream through the dev server (telemetry/ on disk).
 * Returns the folder, or null when no dev server is listening (a static build): use the download buttons.
 */
export async function saveTelemetry(c: Content, session: FightSession): Promise<string | null> {
  recordAttempt(session);
  const meta = session.record.meta;
  assignAttempt(session);
  const name = telemetryFolderName(new Date(), meta, session.state.outcome?.result ?? 'unfinished');
  try {
    const r = await fetch('/api/telemetry', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ name, files: telemetryFiles(session) }) });
    if (!r.ok) return null;
    const j = (await r.json()) as { dir?: string };
    void c;
    return j.dir ?? null;
  } catch {
    return null;
  }
}

export function download(name: string, content: string, type = 'application/json'): void {
  const blob = new Blob([content], { type });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = name;
  document.body.appendChild(a);
  a.click();
  a.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 2000);
}

export interface Attempt {
  at: string;
  preset: string | null;
  loadout: Loadout;
  outcome: string;
  rounds: number;
  cause: string | null;
}

const KEY = 'lab-escape.attempts';

/** A per-browser list of recent attempts, shown on the loadout screen. Telemetry on disk is the real record. */
export function loadAttempts(): Attempt[] {
  try {
    const v: unknown = JSON.parse(localStorage.getItem(KEY) ?? '[]');
    return Array.isArray(v) ? v.filter(isAttempt) : [];
  } catch {
    return [];
  }
}

/** The fields the loadout screen reads from a stored attempt; an entry without them is dropped rather than crashing the list. */
function isAttempt(v: unknown): v is Attempt {
  if (typeof v !== 'object' || v === null) return false;
  const a = v as Record<string, unknown>;
  return typeof a.at === 'string' && typeof a.outcome === 'string' && typeof a.rounds === 'number' && (a.preset === null || typeof a.preset === 'string');
}

function recordAttempt(session: FightSession): void {
  try {
    const list = loadAttempts();
    list.unshift({
      at: new Date().toISOString(),
      preset: (session.record.meta?.preset as string | undefined) ?? null,
      loadout: session.record.loadout,
      outcome: session.state.outcome?.result ?? 'unfinished',
      rounds: session.state.round,
      cause: session.state.outcome?.cause ?? null,
    });
    localStorage.setItem(KEY, JSON.stringify(list.slice(0, 40)));
  } catch {
    // Storage can be unavailable (private windows); the attempt list is a convenience only.
  }
}

const isCounts = (v: unknown): v is Record<string, number> =>
  typeof v === 'object' && v !== null && !Array.isArray(v) && Object.values(v).every((n) => typeof n === 'number' && Number.isSafeInteger(n) && n >= 0 && n < Number.MAX_SAFE_INTEGER);

/** The tester ID: a `?tester=` in the page's URL wins and is remembered, else the one saved from last time. */
export function loadTester(search: string = window.location.search): string | undefined {
  const fromUrl = cleanTester(new URLSearchParams(search).get('tester'));
  if (fromUrl) {
    savePref('tester', fromUrl);
    return fromUrl;
  }
  return cleanTester(loadPref<unknown>('tester', undefined, (v): v is string => typeof v === 'string'));
}

/**
 * Numbers a finished fight for its tester: one more than the fights that tester has finished in this browser, taken from
 * the counter as it is now (so two tabs finishing in turn get different numbers), and stored in the replay's meta. A fight
 * with no tester gets none. Own properties only: an ID such as "constructor" must not read the object's inherited members.
 */
export function assignAttempt(session: FightSession): void {
  const meta = session.record.meta;
  if (!meta?.tester) return;
  const counts = loadPref('attemptCounts', {}, isCounts);
  const stored = Object.hasOwn(counts, meta.tester) ? counts[meta.tester]! : 0;
  const attempt = Math.max(stored, unsavedAttempts.get(meta.tester) ?? 0) + 1;
  meta.attempt = attempt;
  // When storage refuses the write, this page keeps the count itself, so a fight is never numbered like the one before it.
  if (savePref('attemptCounts', { ...counts, [meta.tester]: attempt })) unsavedAttempts.delete(meta.tester);
  else unsavedAttempts.set(meta.tester, attempt);
}

/** Attempt numbers storage would not keep, by tester, for the life of this page. */
const unsavedAttempts = new Map<string, number>();

/**
 * Reads a saved preference. Storage outlives the code that wrote it (an older version, a hand edit, a corrupt write),
 * so a value that does not pass `valid` is treated as absent and the fallback is used: the app must start either way.
 */
export function loadPref<T>(key: string, fallback: T, valid: (v: unknown) => v is T): T {
  try {
    const raw = localStorage.getItem(`lab-escape.${key}`);
    if (raw === null) return fallback;
    const v: unknown = JSON.parse(raw);
    return valid(v) ? v : fallback;
  } catch {
    return fallback;
  }
}

/** Saves a preference. Returns false when storage refused it (a private window, a full disk); callers that cannot lose the value keep their own copy. */
export function savePref(key: string, value: unknown): boolean {
  try {
    localStorage.setItem(`lab-escape.${key}`, JSON.stringify(value));
    return true;
  } catch {
    return false;
  }
}
