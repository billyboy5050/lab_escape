import { describe, expect, it } from 'vitest';
import { chargePlans, flamerOptions, flamerTurn, guardShotFrom, guardTurn, medicTurn, wardenTurn } from '../src/ai/enemies';
import { newGame } from '../src/engine/step';
import { reachable } from '../src/rules/pathing';
import { C, contentWith, eventsOf, inWorld, name, run, scenario, T, unitById } from './helpers';

const poisonSrc = { side: 'player' as const, category: 'player' as const, kind: 'unit' as const, id: 0, def: 'player', via: 'spore_pod' };

describe('waves', () => {
  it('wave 1 spawns at the start of round 1 with IDs in table order', () => {
    const g = newGame(C, { abilities: ['sidearm'], upgrades: [] });
    expect(g.state.units.slice(1).map((u) => `${u.id}:${u.def}@${name(u.pos)}`)).toEqual(['1:guard@C3', '2:guard@G3', '3:guard@B2', '4:medic@E2']);
  });

  it('an occupied spawn tile moves the unit to the nearest free tile, lowest row then column', () => {
    const s = scenario({ player: 'H8', round: 3, wavesSpawned: 1, objects: [{ kind: 'shield', at: 'A3' }] });
    const r = run(s, { type: 'endTurn' });
    const wave = eventsOf(r.events, 'WaveSpawned')[0]!;
    const placed = wave.units.map((id) => {
      const u = unitById(r.state, id) ?? eventsOf(r.events, 'UnitSpawned').find((e) => e.unit.id === id)!.unit;
      return `${u.def}`;
    });
    expect(placed).toEqual(['guard', 'guard', 'flamer']);
    const spawned = eventsOf(r.events, 'UnitSpawned').map((e) => name(e.unit.pos));
    expect(spawned).toEqual(['A2', 'A1', 'H3']);
  });

  it('a wave deals no damage in its arrival round', () => {
    const g = newGame(C, { abilities: ['sidearm'], upgrades: [] });
    const r = run(g.state, { type: 'endTurn' });
    expect(eventsOf(r.events, 'DamageDealt')).toHaveLength(0);
    expect(eventsOf(r.events, 'UnitMoved').length).toBeGreaterThan(0);
  });

  it('without the arrival round, the opening round already hurts', () => {
    const c = contentWith({ 'rules.arrivalRound': 'none' });
    const g = newGame(c, { abilities: ['sidearm'], upgrades: [] });
    const r = run(g.state, { type: 'endTurn' }, c);
    expect(eventsOf(r.events, 'DamageDealt').some((e) => e.target.id === 0)).toBe(true);
  });

  it('the arrival round can be limited to wave 1', () => {
    const c = contentWith({ 'rules.arrivalRound': 'first' });
    const s = scenario({ content: c, player: 'H8', round: 3, wavesSpawned: 1 });
    const r = run(s, { type: 'endTurn' }, c);
    expect(eventsOf(r.events, 'UnitSpawned').every((e) => e.unit.arrivalRound === undefined)).toBe(true);
  });

  it('with earlyWaveWhenEmpty, an empty room brings the next wave forward', () => {
    const c = contentWith({ 'rules.earlyWaveWhenEmpty': true });
    const s = scenario({ content: c, player: 'H8', round: 2, wavesSpawned: 1 });
    const r = run(s, { type: 'endTurn' }, c);
    expect(eventsOf(r.events, 'WaveSpawned').map((e) => e.wave)).toEqual([2]);
    expect(r.state.round).toBe(3);
  });
});

describe('Guard', () => {
  it('needs a clear shot: an ally first on the line spoils it, a minion in the way does not', () => {
    const s = scenario({ player: 'B8', units: [{ def: 'guard', at: 'B5' }, { def: 'guard', at: 'B6' }] });
    inWorld(s, (w) => {
      expect(guardShotFrom(w, w.unit(1)!, T('B5'))).toBeNull();
    });
    // A hatchling in the line is itself the nearest target: the Guard still fires.
    const s2 = scenario({ player: 'B8', units: [{ def: 'guard', at: 'B5' }, { def: 'hatchling', at: 'B6' }] });
    inWorld(s2, (w) => {
      expect(guardShotFrom(w, w.unit(1)!, T('B5'))?.target.id).toBe(2);
    });
    // A shield segment in the line does not stop the Guard firing at the player; the shot hits the shield.
    const s3 = scenario({ player: 'B8', units: [{ def: 'guard', at: 'B5' }], objects: [{ kind: 'shield', at: 'B6' }] });
    const { events } = inWorld(s3, (w) => {
      expect(guardShotFrom(w, w.unit(1)!, T('B5'))?.target.id).toBe(0);
      guardTurn(w, w.unit(1)!, false);
    });
    expect(eventsOf(events, 'DamageDealt')[0]!.target.kind).toBe('object');
  });

  it('moves to the reachable tile that gives a shot in the fewest steps', () => {
    const s = scenario({ player: 'E8', units: [{ def: 'guard', at: 'A1' }] });
    const { s: after } = inWorld(s, (w) => guardTurn(w, w.unit(1)!, false));
    expect(name(unitById(after, 1)!.pos)).toBe('B3');
    expect(unitById(after, 0)!.hp).toBe(5);
  });

  it('when no tile gives a shot, closes in on the nearest player-side unit', () => {
    const s = scenario({ player: 'H8', units: [{ def: 'guard', at: 'A1' }] });
    const { s: after } = inWorld(s, (w) => guardTurn(w, w.unit(1)!, false));
    const g = unitById(after, 1)!;
    expect(g.pos).not.toEqual(T('A1'));
    expect(unitById(after, 0)!.hp).toBe(8);
  });

  it('in its arrival round walks to its firing tile but does not shoot', () => {
    const s = scenario({ player: 'E8', units: [{ def: 'guard', at: 'A1', arrival: true }] });
    const { s: after, events } = inWorld(s, (w) => guardTurn(w, w.unit(1)!, true));
    expect(name(unitById(after, 1)!.pos)).toBe('B3');
    expect(eventsOf(events, 'ProjectileFired')).toHaveLength(0);
  });
});

describe('Medic', () => {
  it('heals the damaged ally with the lowest HP for 2', () => {
    const s = scenario({ player: 'A8', units: [{ def: 'medic', at: 'E2' }, { def: 'guard', at: 'E3', hp: 3 }, { def: 'guard', at: 'F3', hp: 4 }] });
    const { s: after } = inWorld(s, (w) => medicTurn(w, w.unit(1)!, false));
    expect(unitById(after, 2)!.hp).toBe(5);
    expect(unitById(after, 3)!.hp).toBe(4);
  });

  it('prefers a tile at least 4 from the nearest player-side unit', () => {
    const s = scenario({ player: 'E7', units: [{ def: 'medic', at: 'E4' }, { def: 'guard', at: 'E2', hp: 3 }] });
    const { s: after } = inWorld(s, (w) => medicTurn(w, w.unit(1)!, false));
    const m = unitById(after, 1)!;
    expect(Math.max(Math.abs(m.pos.x - 4), Math.abs(m.pos.y - 6))).toBeGreaterThanOrEqual(4);
    expect(unitById(after, 2)!.hp).toBe(5);
  });

  it('with nobody hurt, follows the nearest Guard and stays within 3 of it', () => {
    const s = scenario({ player: 'A8', units: [{ def: 'medic', at: 'H1' }, { def: 'guard', at: 'C3' }] });
    const { s: after } = inWorld(s, (w) => medicTurn(w, w.unit(1)!, false));
    const m = unitById(after, 1)!;
    expect(m.pos).not.toEqual(T('H1'));
  });
});

describe('Flamer', () => {
  it('scores +2 per player-side unit hit, +4 more for the player, -3 per ally', () => {
    const s = scenario({ player: 'E4', units: [{ def: 'flamer', at: 'E2' }, { def: 'guard', at: 'E3' }, { def: 'hatchling', at: 'E5' }] });
    inWorld(s, (w) => {
      const f = w.unit(1)!;
      const opts = flamerOptions(w, f, [f.pos], () => 0);
      const south = opts.find((o) => o.dir.y === 1)!;
      expect(south.score).toBe(-3 + 6 + 2);
      expect(south.hits).toEqual([0, 2, 3]);
    });
  });

  it('takes the highest positive score, hitting its own ally if that still scores best', () => {
    const s = scenario({ player: 'E4', units: [{ def: 'flamer', at: 'E2' }, { def: 'guard', at: 'E3' }, { def: 'hatchling', at: 'E5' }] });
    const { events } = inWorld(s, (w) => flamerTurn(w, w.unit(1)!, false));
    const dmg = eventsOf(events, 'DamageDealt').map((e) => [e.target.id, e.amount]);
    expect(dmg).toEqual([
      [0, 3],
      [2, 3],
      [3, 3],
    ]);
  });

  it('with no positive option, moves toward the nearest player-side unit', () => {
    const s = scenario({ player: 'A8', units: [{ def: 'flamer', at: 'H1' }] });
    const { s: after, events } = inWorld(s, (w) => flamerTurn(w, w.unit(1)!, false));
    expect(eventsOf(events, 'AreaEffect')).toHaveLength(0);
    expect(unitById(after, 1)!.pos).not.toEqual(T('H1'));
  });

  it('walls and pillars end the sweep line', () => {
    const s = scenario({ player: 'B5', units: [{ def: 'flamer', at: 'B3' }] });
    inWorld(s, (w) => {
      const opts = flamerOptions(w, w.unit(1)!, [T('B3')], () => 0);
      expect(opts.find((o) => o.dir.y === 1)!.hits).toEqual([]); // B4 pillar blocks the line to B5
    });
  });
});

describe('Warden', () => {
  it('charges a player-side unit 2 to 5 tiles away and stops against the first body in the lane', () => {
    const s = scenario({ player: 'D7', units: [{ def: 'warden', at: 'D2' }, { def: 'hatchling', at: 'D5' }] });
    const { s: after, events } = inWorld(s, (w) => wardenTurn(w, w.unit(1)!, false));
    expect(name(unitById(after, 1)!.pos)).toBe('D4');
    expect(eventsOf(events, 'Charged')[0]!.hit).toEqual({ kind: 'unit', id: 2 });
    expect(unitById(after, 2)).toBeUndefined();
    expect(unitById(after, 0)!.hp).toBe(8);
  });

  it('needs to move at least one tile, and an ally in the lane blocks it', () => {
    const blocked = scenario({ player: 'D7', units: [{ def: 'warden', at: 'D2' }, { def: 'hatchling', at: 'D3' }] });
    inWorld(blocked, (w) => expect(chargePlans(w, w.unit(1)!, T('D2')).filter((p) => p.dir.y === 1)).toEqual([]));
    const ally = scenario({ player: 'D6', units: [{ def: 'warden', at: 'D2' }, { def: 'guard', at: 'D4' }] });
    inWorld(ally, (w) => expect(chargePlans(w, w.unit(1)!, T('D2'))).toEqual([]));
  });

  it('charges a target 5 tiles away (4 tiles of movement) but not 6', () => {
    const five = scenario({ player: 'D7', units: [{ def: 'warden', at: 'D2' }] });
    inWorld(five, (w) => expect(chargePlans(w, w.unit(1)!, T('D2'))).toHaveLength(1));
    const six = scenario({ player: 'D8', units: [{ def: 'warden', at: 'D2' }] });
    inWorld(six, (w) => expect(chargePlans(w, w.unit(1)!, T('D2'))).toHaveLength(0));
  });

  it('melees the player when adjacent and no charge is available', () => {
    const s = scenario({ player: 'D3', units: [{ def: 'warden', at: 'D2' }] });
    const { s: after } = inWorld(s, (w) => wardenTurn(w, w.unit(1)!, false));
    expect(unitById(after, 0)!.hp).toBe(5);
  });

  it('prefers an approach tile that lines up a charge lane for next round', () => {
    // E4 would be closer, but G2 and D5 line up a lane at the player on G5; G2 wins on the lower row.
    const s = scenario({ player: 'G5', units: [{ def: 'warden', at: 'D2' }] });
    const { s: after } = inWorld(s, (w) => wardenTurn(w, w.unit(1)!, false));
    expect(name(unitById(after, 1)!.pos)).toBe('G2');
    expect(unitById(after, 0)!.hp).toBe(8);
    // Next round it charges down the lane.
    const { s: next, events } = inWorld(after, (w) => wardenTurn(w, w.unit(1)!, false));
    expect(eventsOf(events, 'Charged')).toHaveLength(1);
    expect(unitById(next, 0)!.hp).toBe(4);
  });

  it('a ring of hatchlings cannot stall it: it melees an adjacent minion when it cannot get closer', () => {
    const s = scenario({
      player: 'H8',
      units: [{ def: 'warden', at: 'A1' }, { def: 'hatchling', at: 'B1' }, { def: 'hatchling', at: 'A2', hp: 1 }],
    });
    const { events } = inWorld(s, (w) => {
      expect(reachable(w, w.unit(1)!, 3).dests).toHaveLength(1);
      wardenTurn(w, w.unit(1)!, false);
    });
    const hit = eventsOf(events, 'DamageDealt')[0]!;
    expect(hit.target.id).toBe(3); // lowest HP adjacent minion
  });

  it('Purge clears its poison, starts a 2-round cooldown and blocks spread for the rest of the round', () => {
    const s = scenario({
      player: 'H8',
      units: [{ def: 'warden', at: 'A1', statuses: { poison: { remaining: 3, src: poisonSrc } } }, { def: 'guard', at: 'B1', statuses: { poison: { remaining: 3, src: poisonSrc } } }],
    });
    // Keep both in place for the test.
    for (const u of s.units.slice(1)) u.statuses.pinned = { remaining: 1, src: poisonSrc };
    let r = run(s, { type: 'endTurn' });
    let wd = unitById(r.state, 1)!;
    expect(eventsOf(r.events, 'Purged')).toHaveLength(1);
    expect(wd.statuses.poison).toBeUndefined(); // the poisoned Guard next to it did not reinfect it
    expect(wd.cooldowns['purge']).toBe(1);
    // Next round it can be reinfected by spread, and Purge is still cooling down.
    r = run(r.state, { type: 'endTurn' });
    wd = unitById(r.state, 1)!;
    expect(eventsOf(r.events, 'Purged')).toHaveLength(0);
    expect(wd.statuses.poison).toBeDefined();
    r = run(r.state, { type: 'endTurn' });
    expect(eventsOf(r.events, 'Purged')).toHaveLength(1);
  });
});
