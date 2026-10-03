import type { Content } from '../content/types';
import { upgradeAllowed } from '../content/load';
import { ABILITIES, matchOption, type TargetOption } from '../effects/abilities';
import { spawnWave } from '../rules/waves';
import { FightOver, IllegalCommand, type World } from '../rules/world';
import { cheb, manhattan, samePos } from '../state/grid';
import { cloneState, createInitialState } from '../state/state';
import type { Command, FightSettings, Frame, GameEvent, GameState, Loadout, Pos } from '../state/types';
import { isTileName, parseTile } from '../util/tiles';
import { createWorld, endTurn, startRound } from './phases';

export interface StepResult {
  ok: boolean;
  error?: string;
  state: GameState;
  events: GameEvent[];
  /** One frame per root action, each with a state snapshot. Only filled when frames were requested. */
  frames: Frame[];
  /** The command spent AP or set something off, so free movement before it can no longer be undone. */
  commits: boolean;
}

export interface StepOptions {
  frames?: boolean;
}

/**
 * The engine's single entry point: a pure function from a state and a command to a new state and the
 * events it produced. The input state is never modified. Illegal commands return ok: false.
 */
export function step(c: Content, s0: GameState, cmd: Command, opts: StepOptions = {}): StepResult {
  const s = cloneState(s0);
  const w = createWorld(c, s, { frames: opts.frames });
  try {
    applyCommand(w, cmd);
  } catch (e) {
    if (e instanceof IllegalCommand) return { ok: false, error: e.message, state: s0, events: [], frames: [], commits: false };
    if (!(e instanceof FightOver)) throw e;
  }
  w.closeFrame('tail', null);
  const commits = cmd.type !== 'move' || w.triggered;
  return { ok: true, state: s, events: w.events, frames: w.frames ?? [], commits };
}

export interface NewGameResult {
  state: GameState;
  events: GameEvent[];
  frames: Frame[];
}

/** Creates a fight for a loadout and runs the start of round 1 (wave 1 spawns, the briefing line plays). */
export function newGame(c: Content, loadout: Loadout, settings: FightSettings = {}, opts: StepOptions = {}): NewGameResult {
  const problems = loadoutProblems(c, loadout);
  if (problems.length) throw new Error(`Invalid loadout: ${problems.join('; ')}`);
  const s = createInitialState(c, loadout, settings);
  const w = createWorld(c, s, { frames: opts.frames });
  try {
    startRound(w, 1);
  } catch (e) {
    if (!(e instanceof FightOver)) throw e;
  }
  w.closeFrame('tail', null);
  return { state: s, events: w.events, frames: w.frames ?? [] };
}

/** Loadout rules: up to N of the pool's abilities, up to M upgrades whose required abilities are present, no duplicates. */
export function loadoutProblems(c: Content, l: Loadout): string[] {
  const p: string[] = [];
  const max = c.rules.loadout;
  if (new Set(l.abilities).size !== l.abilities.length) p.push('Duplicate ability');
  if (new Set(l.upgrades).size !== l.upgrades.length) p.push('Duplicate upgrade');
  if (l.abilities.length > max.abilities) p.push(`More than ${max.abilities} abilities`);
  if (l.upgrades.length > max.upgrades) p.push(`More than ${max.upgrades} upgrades`);
  for (const a of l.abilities) if (!c.abilities[a]) p.push(`Unknown ability "${a}"`);
  for (const u of l.upgrades) {
    const def = c.upgrades[u];
    if (!def) p.push(`Unknown upgrade "${u}"`);
    else if (!upgradeAllowed(def.requires, l.abilities)) p.push(`${def.name} needs ${describeRequires(c, def.requires)}`);
  }
  return p;
}

export function describeRequires(c: Content, r: { all?: string[]; any?: string[] }): string {
  const name = (id: string) => c.abilities[id]?.name ?? id;
  const parts: string[] = [];
  if (r.all?.length) parts.push(r.all.map(name).join(' and '));
  if (r.any?.length) parts.push(r.any.map(name).join(' or '));
  return parts.join(', and ');
}

function tile(name: unknown, what: string): Pos {
  if (!isTileName(name)) throw new IllegalCommand(`${what} is not a tile`);
  return parseTile(name);
}

function applyCommand(w: World, cmd: Command): void {
  const s = w.s;
  if (s.outcome) throw new IllegalCommand('The fight is over');
  if (cmd.type === 'debug') return applyDebug(w, cmd);
  if (s.phase !== 'player') throw new IllegalCommand(`Not the player phase (${s.phase})`);
  const p = w.player;
  if (!p) throw new IllegalCommand('The player is not on the board');
  switch (cmd.type) {
    case 'move': {
      if (!Array.isArray(cmd.path) || cmd.path.length === 0) throw new IllegalCommand('Move needs a path');
      const path = cmd.path.map((t) => tile(t, 'Path step'));
      if (path.length > s.movement) throw new IllegalCommand('Not enough movement');
      let prev = p.pos;
      for (const t of path) {
        if (manhattan(prev, t) !== 1) throw new IllegalCommand('Each step must be to an orthogonally adjacent tile');
        if (!w.isFree(t, p.id)) throw new IllegalCommand(`The path is blocked at ${String.fromCharCode(65 + t.x)}${t.y + 1}`);
        prev = t;
      }
      s.movement -= path.length;
      w.root('move', 0, () => {
        w.moveAlong(p, path, 'walk');
      });
      if (w.triggered) s.committed = true;
      return;
    }
    case 'sprint': {
      const sp = w.c.rules.sprint;
      if (s.ap < sp.ap) throw new IllegalCommand('Not enough AP');
      s.ap -= sp.ap;
      s.movement += sp.movement;
      s.committed = true;
      w.root('sprint', 0, () => w.emit({ t: 'Sprinted', movement: sp.movement }));
      return;
    }
    case 'ability': {
      const def = w.c.abilities[cmd.ability];
      const impl = ABILITIES[cmd.ability];
      if (!def || !impl) throw new IllegalCommand(`Unknown ability "${cmd.ability}"`);
      if (!s.loadout.abilities.includes(cmd.ability)) throw new IllegalCommand(`${def.name} is not in the loadout`);
      if (s.ap < def.ap) throw new IllegalCommand('Not enough AP');
      const want: TargetOption = {
        target: tile(cmd.target, 'Target'),
        target2: cmd.target2 ? tile(cmd.target2, 'Second target') : null,
        mode: cmd.mode,
      };
      const opt = matchOption(impl.options(w, p), want);
      if (!opt) throw new IllegalCommand(impl.blocked?.(w) ?? `Not a valid target for ${def.name}`);
      s.ap -= def.ap;
      s.committed = true;
      w.root(def.name, 0, () => {
        w.emit({ t: 'AbilityUsed', ability: cmd.ability, actor: 0, target: opt.target, target2: opt.target2 ?? undefined, mode: opt.mode, ap: def.ap });
        impl.resolve(w, p, opt);
      });
      return;
    }
    case 'reload': {
      const u = utility(w, 'reload');
      const t = tile(cmd.target, 'Target');
      const turret = w.unitAt(t);
      if (!turret || turret.team !== 'player' || turret.maxAmmo === undefined) throw new IllegalCommand('No turret there');
      if (cheb(turret.pos, p.pos) > (u.range ?? 2)) throw new IllegalCommand('Turret out of range');
      if ((turret.ammo ?? 0) >= turret.maxAmmo) throw new IllegalCommand('Turret is already full');
      s.ap -= u.ap;
      s.committed = true;
      w.root('reload', 0, () => {
        turret.ammo = turret.maxAmmo;
        w.emit({ t: 'Reloaded', id: turret.id, ammo: turret.ammo! });
      });
      return;
    }
    case 'pickUpMine': {
      const u = utility(w, 'pick_up_mine');
      const t = tile(cmd.target, 'Target');
      const mine = w.mineAt(t);
      if (!mine) throw new IllegalCommand('No mine there');
      if (cheb(t, p.pos) > (u.range ?? 1)) throw new IllegalCommand('Mine is not adjacent');
      s.ap -= u.ap;
      s.committed = true;
      w.root('pick up mine', 0, () => w.removeObject(mine, 'pickup'));
      return;
    }
    case 'redeploy': {
      const u = utility(w, 'redeploy');
      const drone = w.unit(cmd.drone);
      if (!drone || drone.droneState !== 'docked') throw new IllegalCommand('That drone is not docked');
      const t = tile(cmd.target, 'Target');
      if (cheb(t, p.pos) !== 1 || !w.isFree(t)) throw new IllegalCommand('Redeploy needs a free tile adjacent to you');
      s.ap -= u.ap;
      s.committed = true;
      w.root('redeploy', 0, () => {
        drone.pos = { ...t };
        drone.droneState = 'deployed';
        drone.returningPhases = 0;
        drone.charges = drone.maxCharges = w.droneCharges(w.c.units[drone.def]!.charges ?? 1);
        w.emit({ t: 'DroneRedeployed', id: drone.id, pos: { ...t } });
      });
      return;
    }
    case 'endTurn':
      s.committed = true;
      endTurn(w);
      return;
  }
}

function utility(w: World, id: 'reload' | 'pick_up_mine' | 'redeploy') {
  const u = w.c.utilities[id];
  if (!u) throw new IllegalCommand(`Unknown action ${id}`);
  if (u.requiresAbility && !w.s.loadout.abilities.includes(u.requiresAbility)) throw new IllegalCommand(`${u.name} needs ${w.c.abilities[u.requiresAbility]?.name}`);
  if (w.s.ap < u.ap) throw new IllegalCommand('Not enough AP');
  return u;
}

function applyDebug(w: World, cmd: Extract<Command, { type: 'debug' }>): void {
  const s = w.s;
  switch (cmd.op) {
    case 'setHp': {
      const u = w.unit(cmd.unit);
      if (!u) throw new IllegalCommand('No such unit');
      w.root('debug: set HP', null, () => {
        u.hp = Math.max(1, Math.min(u.maxHp, Math.floor(cmd.value)));
        w.emit({ t: 'Debug', op: 'setHp', detail: `${u.def} ${u.id} HP set to ${u.hp}` });
      });
      return;
    }
    case 'setAp':
      w.root('debug: set AP', null, () => {
        s.ap = Math.max(0, Math.floor(cmd.ap));
        if (cmd.movement !== undefined) s.movement = Math.max(0, Math.floor(cmd.movement));
        w.emit({ t: 'Debug', op: 'setAp', detail: `AP ${s.ap}, movement ${s.movement}` });
      });
      return;
    case 'spawn': {
      if (!w.c.units[cmd.def] || cmd.def === 'player') throw new IllegalCommand(`Cannot spawn "${cmd.def}"`);
      const want = tile(cmd.target, 'Target');
      w.root('debug: spawn', null, () => {
        const at = w.findSpawnTile(want);
        if (!at) return;
        const u = w.spawnUnit(cmd.def, at, 'debug');
        w.emit({ t: 'Debug', op: 'spawn', detail: `Spawned ${u.def} ${u.id} at ${String.fromCharCode(65 + at.x)}${at.y + 1}` });
      });
      return;
    }
    case 'forceWave': {
      if (s.wavesSpawned >= w.c.waves.length) throw new IllegalCommand('No waves left');
      w.root('debug: force wave', null, () => {
        const idx = s.wavesSpawned;
        if (s.settings.maxWaves !== undefined && s.settings.maxWaves <= idx) s.settings.maxWaves = idx + 1;
        spawnWave(w, idx);
        w.emit({ t: 'Debug', op: 'forceWave', detail: `Forced wave ${idx + 1}` });
      });
      return;
    }
  }
}

export function sameTile(a: Pos, b: Pos): boolean {
  return samePos(a, b);
}
