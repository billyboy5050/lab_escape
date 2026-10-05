import { describe, expect, it } from 'vitest';
import { newGame } from '../src/engine/step';
import type { SourceRef } from '../src/state/types';
import { C, contentWith, eventsOf, inWorld, run, scenario } from './helpers';

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

  describe('two hpBelow lines for one unit', () => {
    const lines = (c = C) => [
      ...c.files.intercom.lines.filter((l) => l.trigger.type !== 'hpBelow'),
      { id: 'wardenHalf', trigger: { type: 'hpBelow' as const, def: 'warden', threshold: 10 }, text: 'Warden, report.' },
      { id: 'wardenCritical', trigger: { type: 'hpBelow' as const, def: 'warden', threshold: 5 }, text: 'Warden, fall back.' },
    ];
    const c2 = contentWith({ 'intercom.lines': lines() });
    const hit = (hp: number, amount: number) => {
      const s = scenario({ player: 'B8', units: [{ def: 'warden', at: 'B5', hp }], content: c2 });
      const r = inWorld(s, (w) => w.damageUnit(w.unit(1)!, amount, 'acid', src), c2);
      return { lines: eventsOf(r.events, 'IntercomLine').map((e) => e.line), queue: r.s.intercom.queue };
    };

    it('plays only the line whose threshold was crossed, not the lower one as well', () => {
      // 11 to 9 crosses 10 but not 5: the 5 HP warning must not be played or queued early.
      expect(hit(11, 2)).toEqual({ lines: ['wardenHalf'], queue: [] });
    });

    it('plays the lower line when its own threshold is crossed later', () => {
      expect(hit(6, 2)).toEqual({ lines: ['wardenCritical'], queue: [] });
    });

    it('one hit across both thresholds raises both lines, the second queued for the next phase', () => {
      expect(hit(12, 8)).toEqual({ lines: ['wardenHalf'], queue: ['wardenCritical'] });
    });
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
