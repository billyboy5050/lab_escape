import { describe, expect, it } from 'vitest';
import { FightSession } from '../src/telemetry/session';
import { summarize } from '../src/telemetry/summary';
import type { GameState } from '../src/state/types';
import { C, run, scenario } from './helpers';

const LOADOUT = { abilities: ['sidearm', 'proximity_mine'], upgrades: [] };

/** A session on a hand-built state, with a clock the test moves. */
function sessionOn(state: GameState) {
  const clock = { t: 0 };
  const f = new FightSession(C, LOADOUT, {}, {}, () => clock.t);
  f.state = state;
  return { f, clock, times: () => f.record.meta?.turnTimesMs };
}

describe('turn times', () => {
  it('start when the client says the player can act, so the enemy phase playing out is not counted', () => {
    const { f, clock, times } = sessionOn(scenario({ player: 'B8' }));
    clock.t = 5_000; // the opening plays
    f.turnReady();
    clock.t = 15_000;
    f.apply({ type: 'endTurn' });
    clock.t = 25_000; // the minion, enemy and environment phases play out
    f.turnReady();
    clock.t = 30_000;
    f.apply({ type: 'endTurn' });
    expect(times()).toEqual([10_000, 5_000]);
  });

  it('ignore a repeated turnReady in the same turn, such as the end of each action animation', () => {
    const { f, clock, times } = sessionOn(scenario({ player: 'B8' }));
    f.turnReady();
    clock.t = 4_000;
    f.apply({ type: 'move', path: ['B7'] }); // a free move; the animation ends and the client calls turnReady again
    f.turnReady();
    clock.t = 9_000;
    f.apply({ type: 'endTurn' });
    expect(times()).toEqual([9_000]);
  });

  it('are timed from the end of the previous turn when nothing calls turnReady (the simulator, tests)', () => {
    const { f, clock, times } = sessionOn(scenario({ player: 'B8' }));
    clock.t = 10;
    f.apply({ type: 'endTurn' });
    clock.t = 25;
    f.apply({ type: 'endTurn' });
    expect(times()).toEqual([10, 15]);
  });

  it('include the last turn when an action ends the fight', () => {
    const { f, clock, times } = sessionOn(scenario({ player: 'B8', wavesSpawned: 3, units: [{ def: 'guard', at: 'B6', hp: 2 }] }));
    f.turnReady();
    clock.t = 4_000;
    f.apply({ type: 'ability', ability: 'sidearm', target: 'B6' });
    expect(f.state.outcome?.result).toBe('win');
    expect(times()).toEqual([4_000]);
  });

  it('record a turn once when End Turn itself ends the fight', () => {
    const { f, clock, times } = sessionOn(scenario({ player: 'B8', playerHp: 2, units: [{ def: 'guard', at: 'B6' }] }));
    f.turnReady();
    clock.t = 7_000;
    f.apply({ type: 'endTurn' });
    expect(f.state.outcome?.result).toBe('lose');
    expect(times()).toEqual([7_000]);
  });

  it('do not record a turn for an action that leaves the fight going', () => {
    const { f, clock, times } = sessionOn(scenario({ player: 'B8', units: [{ def: 'guard', at: 'B6' }] }));
    f.turnReady();
    clock.t = 3_000;
    f.apply({ type: 'ability', ability: 'sidearm', target: 'B6' });
    expect(f.state.outcome).toBeNull();
    expect(times()).toEqual([]);
  });
});

describe('action usage in the summary', () => {
  it('counts Pick Up Mine, like the other utility actions', () => {
    const s = scenario({ player: 'B8', objects: [{ kind: 'mine', at: 'B7' }] });
    const r = run(s, { type: 'pickUpMine', target: 'B7' });
    expect(summarize(r.events, r.state).abilitiesUsed).toEqual({ pick_up_mine: 1 });
  });

  it('does not count a mine that was set off, or any other removal', () => {
    const s = scenario({ player: 'A7', objects: [{ kind: 'mine', at: 'B7' }] });
    const r = run(s, { type: 'move', path: ['B7'] });
    expect(summarize(r.events, r.state).abilitiesUsed.pick_up_mine).toBeUndefined();
  });
});
