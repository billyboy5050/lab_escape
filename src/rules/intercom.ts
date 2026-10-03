import type { IntercomLineDef, IntercomTrigger } from '../content/types';
import type { Unit } from '../state/types';
import type { World } from './world';

/**
 * Intercom rules: each line plays at most once per fight and at most one line plays per phase.
 * Lines triggered together queue in table order and play one per phase. The player's death line
 * plays at once and clears the queue. Lines raised during a root action wait for it to finish,
 * so lines triggered by the same action play in table order.
 */
export function intercomTrigger(w: World, type: IntercomTrigger['type'], data: { round?: number; def?: string } = {}): void {
  const lines = w.c.intercom.lines;
  const ic = w.s.intercom;
  let changed = false;
  for (const line of lines) {
    if (!matches(line, type, data)) continue;
    if (ic.played.includes(line.id) || ic.queue.includes(line.id)) continue;
    if (line.immediate) {
      ic.queue = [];
      play(w, line);
      return;
    }
    ic.queue.push(line.id);
    changed = true;
  }
  if (changed) {
    const order = new Map(lines.map((l, i) => [l.id, i]));
    ic.queue.sort((a, b) => order.get(a)! - order.get(b)!);
    if (!w.inChain) tryPlay(w);
  }
}

/** Called when a root action finishes: plays the first queued line if none has played this phase. */
export function intercomFlush(w: World): void {
  tryPlay(w);
}

/** Damage that leaves a unit alive below a line's threshold triggers it ("Warden, report."). */
export function intercomHpCheck(w: World, u: Unit, before: number): void {
  for (const line of w.c.intercom.lines) {
    const t = line.trigger;
    if (t.type === 'hpBelow' && t.def === u.def && t.threshold !== undefined && u.hp > 0 && u.hp < t.threshold && before >= t.threshold) {
      intercomTrigger(w, 'hpBelow', { def: u.def });
      return;
    }
  }
}

/** A new phase may play one more line. */
export function intercomPhaseStart(w: World): void {
  w.s.intercom.lineThisPhase = false;
  tryPlay(w);
}

function tryPlay(w: World): void {
  const ic = w.s.intercom;
  if (ic.lineThisPhase || !ic.queue.length) return;
  const id = ic.queue.shift()!;
  const line = w.c.intercom.lines.find((l) => l.id === id);
  if (line) play(w, line);
}

function play(w: World, line: IntercomLineDef): void {
  const ic = w.s.intercom;
  ic.played.push(line.id);
  ic.lineThisPhase = true;
  ic.log.push({ round: w.s.round, phase: w.s.phase, line: line.id, text: line.text });
  w.emit({ t: 'IntercomLine', line: line.id, text: line.text });
}

function matches(line: IntercomLineDef, type: IntercomTrigger['type'], data: { round?: number; def?: string }): boolean {
  const t = line.trigger;
  if (t.type !== type) return false;
  if (t.round !== undefined && t.round !== data.round) return false;
  if (t.def !== undefined && t.def !== data.def) return false;
  return true;
}
