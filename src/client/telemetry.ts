import type { Content } from '../content/types';
import type { Loadout } from '../state/types';
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
  const preset = (session.record.meta?.preset as string | undefined) ?? 'custom';
  const name = `${new Date().toISOString().replace(/[:.]/g, '-')}-${preset}-${session.state.outcome?.result ?? 'unfinished'}`;
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
    return JSON.parse(localStorage.getItem(KEY) ?? '[]') as Attempt[];
  } catch {
    return [];
  }
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

export function loadPref<T>(key: string, fallback: T): T {
  try {
    const v = localStorage.getItem(`lab-escape.${key}`);
    return v === null ? fallback : (JSON.parse(v) as T);
  } catch {
    return fallback;
  }
}

export function savePref(key: string, value: unknown): void {
  try {
    localStorage.setItem(`lab-escape.${key}`, JSON.stringify(value));
  } catch {
    // ignore
  }
}
