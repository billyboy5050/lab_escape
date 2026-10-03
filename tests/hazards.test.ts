import { describe, expect, it } from 'vitest';
import { primeDecisions } from '../src/ai/hazards';
import { createWorld } from '../src/engine/phases';
import type { GameState } from '../src/state/types';
import { C, eventsOf, run, scenario } from './helpers';

function decisions(s: GameState) {
  const w = createWorld(C, structuredClone(s));
  return Object.fromEntries(primeDecisions(w).map((d) => [d.hazard.id, { prime: d.prime, step: d.step, reason: d.reason }]));
}

describe('the value rule (checked at priming)', () => {
  it('step 1: holds whenever an elite would be hit', () => {
    const d = decisions(scenario({ player: 'E5', units: [{ def: 'warden', at: 'D4' }] }));
    expect(d['panel']).toMatchObject({ prime: false, step: 1 });
  });

  it('step 2: a combined kill primes every hazard that would hit the player, even through soldiers', () => {
    // G5 is in the gun lane (2) and next to vent G6 (1): 3 damage against 3 HP. A Guard also stands in the vent area.
    const d = decisions(scenario({ player: 'G5', playerHp: 3, units: [{ def: 'guard', at: 'H7' }] }));
    expect(d['gun']).toMatchObject({ prime: true, step: 2 });
    expect(d['vent_g6']).toMatchObject({ prime: true, step: 2 });
  });

  it('step 2 fails when the total is short of the player HP, and the rule falls through', () => {
    const d = decisions(scenario({ player: 'G5', playerHp: 4, units: [{ def: 'guard', at: 'H7' }] }));
    expect(d['gun']).toMatchObject({ prime: true, step: 3 });
    expect(d['vent_g6']).toMatchObject({ prime: false, step: 4, reason: 'A soldier would be hit' });
  });

  it('step 3: primes when only the player side would be hit', () => {
    const d = decisions(scenario({ player: 'A8', units: [{ def: 'hatchling', at: 'D6' }] }));
    expect(d['panel']).toMatchObject({ prime: true, step: 3 });
  });

  it('step 4: holds when a soldier would be hit or nothing would be hit', () => {
    const d = decisions(scenario({ player: 'C5', units: [{ def: 'guard', at: 'A6' }] }));
    expect(d['vent_b5']).toMatchObject({ prime: false, step: 4, reason: 'A soldier would be hit' });
    expect(d['vent_g6']).toMatchObject({ prime: false, step: 4, reason: 'Nothing to hit' });
  });

  it('the wall gun: a player-side object first in its lane ranks as a minion and draws its fire', () => {
    const d = decisions(scenario({ player: 'A8', objects: [{ kind: 'shield', at: 'E5' }] }));
    expect(d['gun']).toMatchObject({ prime: true, step: 3 });
  });

  it('the wall gun: a soldier first in its lane shields the player behind it', () => {
    const d = decisions(scenario({ player: 'C5', units: [{ def: 'guard', at: 'F5' }] }));
    expect(d['gun']).toMatchObject({ prime: false, step: 4 });
  });

  it('records which step produced each decision for the debug overlay', () => {
    const r = run(scenario({ player: 'A6', units: [{ def: 'guard', at: 'H1' }] }), { type: 'endTurn' });
    const vent = r.state.hazards.find((h) => h.id === 'vent_b5')!;
    expect(vent.last).toMatchObject({ stage: 'prime', result: 'prime', step: 3 });
  });
});

describe('prime, then fire', () => {
  it('a committed hazard fires on a baited soldier', () => {
    // Round 2: the player stands in vent B5's area and draws the cue.
    let s = scenario({ player: 'A6', units: [{ def: 'guard', at: 'H1' }] });
    let r = run(s, { type: 'endTurn' });
    expect(eventsOf(r.events, 'HazardPrimed').map((e) => e.hazard)).toContain('vent_b5');
    s = r.state;
    // Round 3: the player leaves; a soldier now stands in the area when the vent fires.
    s.units[0]!.pos = { x: 0, y: 7 };
    s.units[1]!.pos = { x: 0, y: 4 };
    s.units[1]!.statuses.pinned = { remaining: 1, src: { side: 'player', category: 'player', kind: 'unit', id: 0, def: 'player', via: 'test' } };
    r = run(s, { type: 'endTurn' });
    const fired = eventsOf(r.events, 'HazardFired').find((e) => e.hazard === 'vent_b5');
    expect(fired?.victims).toEqual([{ kind: 'unit', id: 1 }]);
    expect(eventsOf(r.events, 'IntercomLine').map((e) => e.line)).toContain('hazardHitsSoldier');
  });

  it('cancels with a short cooldown if an elite would be hit', () => {
    let s = scenario({ player: 'A6', units: [{ def: 'guard', at: 'H1' }] });
    s = run(s, { type: 'endTurn' }).state;
    s.units[0]!.pos = { x: 0, y: 7 };
    s.units[1] = { ...s.units[1]!, def: 'warden', elite: true, armor: 2, hp: 20, maxHp: 20, pos: { x: 0, y: 4 }, statuses: { pinned: { remaining: 1, src: { side: 'player', category: 'player', kind: 'unit', id: 0, def: 'player', via: 'test' } } }, cooldowns: { purge: 0 } };
    const r = run(s, { type: 'endTurn' });
    const c = eventsOf(r.events, 'HazardCancelled').find((e) => e.hazard === 'vent_b5');
    expect(c?.reason).toBe('An elite would be hit');
  });

  it('cancels if nothing would be hit', () => {
    let s = scenario({ player: 'A6', units: [{ def: 'guard', at: 'H1' }] });
    s = run(s, { type: 'endTurn' }).state;
    s = run(s, { type: 'move', path: ['A7', 'A8'] }).state;
    const r = run(s, { type: 'endTurn' });
    expect(eventsOf(r.events, 'HazardCancelled').find((e) => e.hazard === 'vent_b5')?.reason).toBe('Nothing to hit');
  });

  it('fires in round N, primes again in N+3 and fires in N+4; a cancel in round N can prime again in N+1', () => {
    const boxed = (s: GameState) => {
      s.objects.push(
        { id: 90, kind: 'shield', pos: { x: 6, y: 0 }, team: 'player', hp: 99, maxHp: 99, placement: 9, createdRound: 1 },
        { id: 91, kind: 'shield', pos: { x: 6, y: 1 }, team: 'player', hp: 99, maxHp: 99, placement: 9, createdRound: 1 },
      );
      return s;
    };
    let s = boxed(scenario({ player: 'A6', round: 1, units: [{ def: 'guard', at: 'H1' }] }));
    const primed: number[] = [];
    const fired: number[] = [];
    for (let i = 0; i < 5; i++) {
      const round = s.round;
      const r = run(s, { type: 'endTurn' });
      if (eventsOf(r.events, 'HazardPrimed').some((e) => e.hazard === 'vent_b5')) primed.push(round);
      if (eventsOf(r.events, 'HazardFired').some((e) => e.hazard === 'vent_b5')) fired.push(round);
      s = r.state;
    }
    expect(primed).toEqual([1, 5]);
    expect(fired).toEqual([2]);
    const r6 = run(s, { type: 'endTurn' });
    expect(eventsOf(r6.events, 'HazardFired').some((e) => e.hazard === 'vent_b5')).toBe(true);

    // Cancel timing.
    let t = boxed(scenario({ player: 'A6', round: 1, units: [{ def: 'guard', at: 'H1' }] }));
    t = run(t, { type: 'endTurn' }).state; // round 1: primes
    t = run(t, { type: 'move', path: ['A7', 'A8'] }).state;
    let r = run(t, { type: 'endTurn' }); // round 2: cancels (nothing in the area)
    expect(eventsOf(r.events, 'HazardCancelled').some((e) => e.hazard === 'vent_b5')).toBe(true);
    t = r.state;
    t = run(t, { type: 'move', path: ['A7', 'A6'] }).state;
    r = run(t, { type: 'endTurn' }); // round 3: primes again
    expect(eventsOf(r.events, 'HazardPrimed').some((e) => e.hazard === 'vent_b5')).toBe(true);
  });

  it('vent gas is one hit of poison-type damage that ignores armor and does not apply Poison', () => {
    let s = scenario({ player: 'A6', units: [{ def: 'guard', at: 'H1' }] });
    s = run(s, { type: 'endTurn' }).state;
    const r = run(s, { type: 'endTurn' });
    const hit = eventsOf(r.events, 'DamageDealt').find((e) => e.source.kind === 'hazard');
    expect(hit).toMatchObject({ damageType: 'poison', amount: 1 });
    expect(eventsOf(r.events, 'StatusApplied').some((e) => e.status === 'poison')).toBe(false);
  });

  it('the panel grid discharges onto every unit on or adjacent to D5 or E5', () => {
    let s = scenario({ player: 'F4', units: [{ def: 'hatchling', at: 'C5' }, { def: 'guard', at: 'H1' }] });
    s = run(s, { type: 'endTurn' }).state;
    // Pin everyone in place for the firing round.
    const pin = { remaining: 1, src: { side: 'player' as const, category: 'player' as const, kind: 'unit' as const, id: 0, def: 'player', via: 'test' } };
    for (const u of s.units) if (u.id !== 0) u.statuses.pinned = { ...pin };
    s.units[0]!.pos = { x: 5, y: 3 };
    const r = run(s, { type: 'endTurn' });
    const fired = eventsOf(r.events, 'HazardFired').find((e) => e.hazard === 'panel');
    expect(fired).toBeDefined();
    expect(eventsOf(r.events, 'DamageDealt').filter((e) => e.source.id === 'panel').every((e) => e.damageType === 'electric' && e.raw === 2)).toBe(true);
  });
});
