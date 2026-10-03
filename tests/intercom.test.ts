import { describe, expect, it } from 'vitest';
import { newGame } from '../src/engine/step';
import type { SourceRef } from '../src/state/types';
import { C, eventsOf, inWorld, run, scenario } from './helpers';

const src: SourceRef = { side: 'player', category: 'player', kind: 'unit', id: 0, def: 'player', via: 'test' };

describe('intercom', () => {
  it('plays the round 1 line when the fight starts', () => {
    const g = newGame(C, { abilities: ['sidearm'], upgrades: [] });
    expect(eventsOf(g.events, 'IntercomLine').map((e) => e.line)).toEqual(['round1']);
    expect(g.state.intercom.log[0]).toMatchObject({ round: 1, line: 'round1' });
  });

  it('plays the vague wave hints at the start of rounds 3 and 6', () => {
    let s = scenario({ player: 'A8', round: 2, units: [{ def: 'guard', at: 'H1' }] });
    s.objects.push(
      { id: 90, kind: 'shield', pos: { x: 6, y: 0 }, team: 'player', hp: 99, maxHp: 99, placement: 9, createdRound: 2 },
      { id: 91, kind: 'shield', pos: { x: 6, y: 1 }, team: 'player', hp: 99, maxHp: 99, placement: 9, createdRound: 2 },
    );
    const lines: string[] = [];
    for (let i = 0; i < 4; i++) {
      const r = run(s, { type: 'endTurn' });
      lines.push(...eventsOf(r.events, 'IntercomLine').map((e) => `${r.state.round}:${e.line}`));
      s = r.state;
    }
    expect(lines.filter((l) => l.endsWith('round3') || l.endsWith('round6'))).toEqual(['3:round3', '6:round6']);
  });

  it('plays at most one line per phase; the rest queue in table order', () => {
    const s = scenario({ player: 'A8', units: [{ def: 'flamer', at: 'E3', hp: 1 }, { def: 'medic', at: 'F3', hp: 1 }, { def: 'guard', at: 'H8' }] });
    const { s: after, events } = inWorld(s, (w) => {
      w.areaDamage([{ x: 4, y: 2 }, { x: 5, y: 2 }], 5, 'acid', src);
    });
    // Medic is down comes before Burner is down in the table, so it plays first; the other waits.
    expect(eventsOf(events, 'IntercomLine').map((e) => e.line)).toEqual(['medicDies']);
    expect(after.intercom.queue).toEqual(['flamerDies']);
  });

  it('a queued line plays in the next phase', () => {
    const s = scenario({ player: 'A8', units: [{ def: 'flamer', at: 'E3', hp: 1 }, { def: 'medic', at: 'F3', hp: 1 }, { def: 'guard', at: 'H8' }] });
    const { s: mid } = inWorld(s, (w) => w.areaDamage([{ x: 4, y: 2 }, { x: 5, y: 2 }], 5, 'acid', src));
    const r = run(mid, { type: 'endTurn' });
    expect(eventsOf(r.events, 'IntercomLine')[0]?.line).toBe('flamerDies');
  });

  it("the player's death line plays at once and clears the queue", () => {
    const s = scenario({ player: 'B8', playerHp: 3, units: [{ def: 'guard', at: 'B6' }] });
    s.intercom.queue = ['flamerDies'];
    s.intercom.lineThisPhase = true;
    const r = run(s, { type: 'endTurn' });
    const lines = eventsOf(r.events, 'IntercomLine').map((e) => e.line);
    expect(lines[lines.length - 1]).toBe('playerDies');
    expect(r.state.intercom.queue).toEqual([]);
  });

  it('each line plays at most once per fight', () => {
    const s = scenario({ player: 'A6', units: [{ def: 'guard', at: 'H1' }] });
    s.intercom.played.push('firstHazardPrimed');
    const r = run(s, { type: 'endTurn' });
    expect(eventsOf(r.events, 'HazardPrimed').length).toBeGreaterThan(0);
    expect(eventsOf(r.events, 'IntercomLine').some((e) => e.line === 'firstHazardPrimed')).toBe(false);
  });

  it('reports the Warden below 10 HP and the player below 4 HP', () => {
    const s = scenario({ player: 'B8', playerHp: 5, units: [{ def: 'warden', at: 'B5', hp: 11 }] });
    const { events } = inWorld(s, (w) => {
      w.damageUnit(w.unit(1)!, 2, 'acid', src);
      w.damageUnit(w.player!, 2, 'acid', src);
    });
    expect(eventsOf(events, 'IntercomLine').map((e) => e.line)).toEqual(['wardenLow']);
  });
});
