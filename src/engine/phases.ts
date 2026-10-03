import { enemyTurn } from '../ai/enemies';
import { fireStep, primeStep } from '../ai/hazards';
import { minionTurn } from '../ai/minions';
import { registerHooks } from '../effects/hooks';
import type { Content } from '../content/types';
import { decayStep, eggStep, statusTickStep } from '../rules/environment';
import { intercomPhaseStart, intercomTrigger } from '../rules/intercom';
import { spawnDueWaves } from '../rules/waves';
import { World, type WorldOptions } from '../rules/world';
import type { GameState, Phase } from '../state/types';

export function createWorld(c: Content, s: GameState, opts: WorldOptions = {}): World {
  const w = new World(c, s, opts);
  registerHooks(w);
  return w;
}

export function setPhase(w: World, phase: Phase): void {
  w.s.phase = phase;
  w.emit({ t: 'PhaseStarted', round: w.s.round, phase });
  intercomPhaseStart(w);
}

/** Round start: spawn any wave due, play any intercom hint, refill the player's AP and movement. */
export function startRound(w: World, n: number): void {
  w.s.round = n;
  w.emit({ t: 'RoundStarted', round: n });
  setPhase(w, 'roundStart');
  w.root('round start', null, () => spawnDueWaves(w));
  intercomTrigger(w, 'roundStart', { round: n });
  w.s.ap = w.c.rules.turn.ap;
  w.s.movement = w.c.rules.turn.movement;
  w.s.committed = false;
  setPhase(w, 'player');
  w.closeFrame('player phase', 0);
}

/** Minion phase: each minion acts once, in summon order. Units spawned during the phase wait for next round. */
export function runMinionPhase(w: World): void {
  setPhase(w, 'minion');
  const ids = w.s.units.filter((u) => u.kind === 'minion' && !u.dead).map((u) => u.id);
  for (const id of ids) {
    const m = w.unit(id);
    if (!m || m.dead || m.droneState === 'docked') continue;
    w.root(`${m.def} ${m.id}`, m.id, () => minionTurn(w, m));
  }
}

/** Enemy phase: each enemy acts once, in spawn order. */
export function runEnemyPhase(w: World): void {
  setPhase(w, 'enemy');
  const ids = w.s.units.filter((u) => u.team === 'enemy' && !u.dead).map((u) => u.id);
  for (const id of ids) {
    const e = w.unit(id);
    if (!e || e.dead) continue;
    w.root(`${e.def} ${e.id}`, e.id, () => enemyTurn(w, e));
  }
}

/** Environment phase, in order: primed hazards fire or cancel; hazards prime; statuses tick and poison spreads; eggs; cooldowns and decay. */
export function runEnvironmentPhase(w: World): void {
  setPhase(w, 'environment');
  w.root('hazards fire', null, () => fireStep(w));
  w.root('hazards prime', null, () => primeStep(w));
  w.root('statuses tick', null, () => statusTickStep(w));
  w.root('eggs', null, () => eggStep(w));
  w.root('cooldowns and decay', null, () => decayStep(w));
}

/** Everything after the player ends the turn, through to the next player phase (or the end of the fight). */
export function endTurn(w: World): void {
  runMinionPhase(w);
  runEnemyPhase(w);
  runEnvironmentPhase(w);
  if (w.s.round >= w.c.rules.roundCap) {
    w.endFight({ result: 'lose', cause: `Round ${w.c.rules.roundCap} ended with the fight unresolved`, round: w.s.round });
  }
  startRound(w, w.s.round + 1);
}
