import type { Content, DamageType, StatusId, UpgradeDef } from '../content/types';
import { boardFor, cheb, comparePos, samePos, type Board } from '../state/grid';
import { cloneState } from '../state/state';
import type {
  EntityRef,
  Frame,
  GameEvent,
  GameObject,
  GameState,
  MoveMode,
  ObjectKind,
  Outcome,
  Pos,
  SourceRef,
  Unit,
} from '../state/types';
import { intercomFlush, intercomHpCheck, intercomTrigger } from './intercom';

/** Thrown to unwind the engine the moment the fight is decided. The state carries the outcome. */
export class FightOver extends Error {
  constructor(public readonly outcome: Outcome) {
    super(`Fight over: ${outcome.result} (${outcome.cause})`);
    this.name = 'FightOver';
  }
}

/** Thrown for a command the rules do not allow in the current state. The state is left unchanged. */
export class IllegalCommand extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'IllegalCommand';
  }
}

/** Short codes for upgrades, mapped to their content ids. */
export const UP = {
  T1: 'T1_piercing_rounds',
  T2: 'T2_shot_triggered_mines',
  T3: 'T3_suppressing_fire',
  T4: 'T4_grapple_pin',
  T5: 'T5_reflective_shield',
  T6: 'T6_overcharged_drone',
  A1: 'A1_spore_burst',
  A2: 'A2_acid_brood',
  A3: 'A3_parasite_jump',
  A4: 'A4_fast_brood',
  H1: 'H1_spore_mines',
  H2: 'H2_plague_drone',
} as const;
export type UpgradeCode = keyof typeof UP;

interface Job {
  effect: string;
  source: string;
  gen: number;
  run: () => void;
}

interface Chain {
  label: string;
  fired: Set<string>;
  queue: Job[];
  gen: number;
}

/** A death hook may queue follow-up effects for a unit that just died (Spore Burst, Parasite, ...). */
export type DeathHook = (w: World, dead: Unit) => void;

export interface WorldOptions {
  /** Record a state snapshot after every root action, for animation and step-through. */
  frames?: boolean;
}

/**
 * The mutable rules context for one step. It wraps a (cloned) GameState and owns the event list,
 * the chain of the current root action, and every primitive rule: damage, statuses, deaths,
 * movement, spawning and mines. Higher-level behaviour (abilities, AI, phases) is built on it.
 */
export class World {
  readonly b: Board;
  readonly events: GameEvent[] = [];
  readonly frames: Frame[] | null;
  readonly deathHooks: DeathHook[] = [];
  /** True when a command triggered something (a mine), which commits the player's turn. */
  triggered = false;
  private chain: Chain | null = null;
  private frameStart = 0;

  constructor(
    readonly c: Content,
    readonly s: GameState,
    opts: WorldOptions = {},
  ) {
    this.b = boardFor(c.map);
    this.frames = opts.frames ? [] : null;
  }

  // ------------------------------------------------------------------ events and frames

  emit(e: GameEvent): void {
    this.events.push(e);
  }

  /**
   * Runs a root action: a player command, one minion or enemy action, or one environment step.
   * It opens a chain, applies the action, processes deaths, drains the trigger queue, then checks for a win.
   */
  root(label: string, actor: number | null, fn: () => void): void {
    if (this.chain) throw new Error(`Root action "${label}" started inside "${this.chain.label}"`);
    this.chain = { label, fired: new Set(), queue: [], gen: 0 };
    try {
      fn();
      this.flushDeaths();
      this.runQueue();
      this.checkWin();
    } finally {
      // Runs even when the fight ends mid-action (FightOver), so the deciding action keeps its frame
      // and a line queued by it (a win's "Warden is down") still plays.
      this.chain = null;
      try {
        intercomFlush(this);
      } finally {
        this.closeFrame(label, actor);
      }
    }
  }

  /** Groups every event since the last frame into one frame, with a snapshot of the state. */
  closeFrame(label: string, actor: number | null): void {
    if (!this.frames) return;
    if (this.events.length === this.frameStart && label === 'tail') return;
    this.frames.push({ label, phase: this.s.phase, actor, events: this.events.slice(this.frameStart), state: cloneState(this.s) });
    this.frameStart = this.events.length;
  }

  get inChain(): boolean {
    return this.chain !== null;
  }

  /** Queues a triggered effect for the current chain. It runs after the current effect, first in first out. */
  enqueue(effect: string, source: string, run: () => void): void {
    const ch = this.requireChain();
    ch.queue.push({ effect, source, gen: ch.gen + 1, run });
  }

  /**
   * Runs a triggered effect immediately (a mine stepped on mid-move, a reflected shot), under the
   * once-per-chain rule and the depth cap. Deaths it causes are processed before it returns.
   */
  immediate(source: string, effect: string, fn: () => void): boolean {
    const ch = this.requireChain();
    const gen = ch.gen + 1;
    if (!this.admit(ch, source, effect, gen)) return false;
    const prev = ch.gen;
    ch.gen = gen;
    try {
      fn();
      this.flushDeaths();
    } finally {
      ch.gen = prev;
    }
    return true;
  }

  private admit(ch: Chain, source: string, effect: string, gen: number): boolean {
    if (gen > this.c.rules.chainDepthCap) {
      this.emit({ t: 'ChainSuppressed', effect, source, reason: 'depth' });
      return false;
    }
    const key = `${source}|${effect}`;
    if (ch.fired.has(key)) {
      this.emit({ t: 'ChainSuppressed', effect, source, reason: 'repeat' });
      return false;
    }
    ch.fired.add(key);
    return true;
  }

  private runQueue(): void {
    const ch = this.requireChain();
    while (ch.queue.length) {
      const job = ch.queue.shift()!;
      if (!this.admit(ch, job.source, job.effect, job.gen)) continue;
      const prev = ch.gen;
      ch.gen = job.gen;
      try {
        job.run();
        this.flushDeaths();
      } finally {
        ch.gen = prev;
      }
    }
  }

  private requireChain(): Chain {
    if (!this.chain) throw new Error('No chain is open: effects must run inside a root action');
    return this.chain;
  }

  // ------------------------------------------------------------------ queries

  get player(): Unit | undefined {
    const p = this.s.units[0];
    return p && p.id === 0 ? p : this.s.units.find((u) => u.id === 0);
  }

  unit(id: number): Unit | undefined {
    return this.s.units.find((u) => u.id === id);
  }

  /** Units on the board: alive and not docked. */
  activeUnits(): Unit[] {
    return this.s.units.filter((u) => !u.dead && u.droneState !== 'docked');
  }

  unitAt(p: Pos): Unit | undefined {
    return this.s.units.find((u) => !u.dead && u.droneState !== 'docked' && u.pos.x === p.x && u.pos.y === p.y);
  }

  objectsAt(p: Pos): GameObject[] {
    return this.s.objects.filter((o) => !o.destroyed && o.pos.x === p.x && o.pos.y === p.y);
  }

  /** Shield segments and eggs block movement and projectiles (not sight). */
  blockingObjectAt(p: Pos): GameObject | undefined {
    return this.s.objects.find((o) => !o.destroyed && (o.kind === 'shield' || o.kind === 'egg') && o.pos.x === p.x && o.pos.y === p.y);
  }

  objectAt(p: Pos, kind: ObjectKind): GameObject | undefined {
    return this.s.objects.find((o) => !o.destroyed && o.kind === kind && o.pos.x === p.x && o.pos.y === p.y);
  }

  mineAt(p: Pos): GameObject | undefined {
    return this.objectAt(p, 'mine');
  }

  /** A tile a unit could end its move on: passable terrain, no unit, no blocking object. */
  isFree(p: Pos, ignoreUnit?: number): boolean {
    if (this.b.blocksMove(p)) return false;
    if (this.blockingObjectAt(p)) return false;
    const u = this.unitAt(p);
    return !u || u.id === ignoreUnit;
  }

  /** A tile something can be placed on: passable terrain with no unit and no object of any kind. */
  isEmpty(p: Pos): boolean {
    return !this.b.blocksMove(p) && !this.unitAt(p) && this.objectsAt(p).length === 0;
  }

  playerSide(): Unit[] {
    return this.activeUnits().filter((u) => u.team === 'player');
  }

  enemies(): Unit[] {
    return this.activeUnits().filter((u) => u.team === 'enemy');
  }

  /** Minions count toward the cap while alive, including docked drones. */
  minionCount(): number {
    return this.s.units.filter((u) => !u.dead && u.kind === 'minion').length;
  }

  hasUp(code: UpgradeCode): boolean {
    return this.s.loadout.upgrades.includes(UP[code]);
  }

  up(code: UpgradeCode): UpgradeDef {
    return this.c.upgrades[UP[code]]!;
  }

  /** Piercing Rounds: kinetic damage from the Sidearm and turrets ignores some armor. */
  piercing(damageType: DamageType | undefined): number {
    return this.hasUp('T1') && damageType === 'kinetic' ? this.up('T1').armorIgnore ?? 0 : 0;
  }

  armorOf(u: Unit): number {
    return Math.max(0, u.armor - (u.statuses.corrode?.stacks ?? 0));
  }

  /** Movement a unit may use in its next move: Pinned stops it, Slowed reduces it. */
  movementAllowance(u: Unit, base?: number): number {
    if (u.statuses.pinned) return 0;
    const m = base ?? this.c.units[u.def]!.move;
    return Math.max(0, m - (u.statuses.slowed ? this.c.rules.statuses.slowed.movementPenalty : 0));
  }

  // ------------------------------------------------------------------ sources

  unitSource(u: Unit, via: string): SourceRef {
    return {
      side: u.team,
      category: u.kind === 'player' ? 'player' : u.kind === 'minion' ? 'minion' : 'enemy',
      kind: 'unit',
      id: u.id,
      def: u.def,
      via,
    };
  }

  /** Effects owned by the player that are not a unit's attack: mines, upgrade hooks, statuses. */
  playerEffect(via: string, kind: 'unit' | 'object' = 'unit', id: number = 0, def = 'player'): SourceRef {
    return { side: 'player', category: 'player', kind, id, def, via };
  }

  // ------------------------------------------------------------------ damage, healing, statuses

  /** Applies damage to a unit after armor. Marks it dead at 0 HP; removal happens at the next flush. */
  damageUnit(u: Unit, raw: number, type: DamageType, src: SourceRef, armorIgnore = 0): number {
    if (u.dead || u.droneState === 'docked' || raw <= 0) return 0;
    let absorbed = 0;
    if (this.c.rules.armorAppliesTo.includes(type)) {
      absorbed = Math.min(raw, Math.max(0, this.armorOf(u) - armorIgnore));
    }
    const amount = raw - absorbed;
    const before = u.hp;
    u.hp = Math.max(0, u.hp - amount);
    const killed = u.hp <= 0;
    if (killed) {
      u.dead = true;
      u.killedBy = src;
    }
    this.emit({
      t: 'DamageDealt',
      target: { kind: 'unit', id: u.id },
      targetDef: u.def,
      targetSide: u.team,
      pos: { ...u.pos },
      source: src,
      damageType: type,
      raw,
      absorbed,
      amount,
      hpLost: before - u.hp,
      hpAfter: u.hp,
      killed,
    });
    if (!killed && amount > 0) intercomHpCheck(this, u, before);
    return amount;
  }

  /** Eggs and shield segments take damage like units, with no armor. */
  damageObject(o: GameObject, raw: number, type: DamageType, src: SourceRef): number {
    if (o.destroyed || o.hp === undefined || raw <= 0) return 0;
    const before = o.hp;
    o.hp = Math.max(0, o.hp - raw);
    const killed = o.hp <= 0;
    if (killed) {
      o.destroyed = true;
      o.killedBy = src;
    }
    this.emit({
      t: 'DamageDealt',
      target: { kind: 'object', id: o.id },
      targetDef: o.kind,
      targetSide: o.team ?? 'player',
      pos: { ...o.pos },
      source: src,
      damageType: type,
      raw,
      absorbed: 0,
      amount: raw,
      hpLost: before - o.hp,
      hpAfter: o.hp,
      killed,
    });
    return raw;
  }

  damageEntity(ref: EntityRef, raw: number, type: DamageType, src: SourceRef, armorIgnore = 0): number {
    if (ref.kind === 'unit') {
      const u = this.unit(ref.id);
      return u ? this.damageUnit(u, raw, type, src, armorIgnore) : 0;
    }
    const o = this.s.objects.find((x) => x.id === ref.id);
    return o ? this.damageObject(o, raw, type, src) : 0;
  }

  /** Damages every unit, then every damageable object, on the given tiles. Deaths wait for the flush. */
  areaDamage(tiles: Pos[], raw: number, type: DamageType, src: SourceRef, opts: { exclude?: number; armorIgnore?: number } = {}): void {
    const units = this.activeUnits()
      .filter((u) => u.id !== opts.exclude && tiles.some((t) => samePos(t, u.pos)))
      .sort((a, b) => a.id - b.id);
    for (const u of units) this.damageUnit(u, raw, type, src, opts.armorIgnore ?? 0);
    const objs = this.s.objects
      .filter((o) => !o.destroyed && o.hp !== undefined && tiles.some((t) => samePos(t, o.pos)))
      .sort((a, b) => a.id - b.id);
    for (const o of objs) this.damageObject(o, raw, type, src);
  }

  heal(u: Unit, amount: number, src: SourceRef): number {
    if (u.dead) return 0;
    const amt = Math.min(amount, u.maxHp - u.hp);
    if (amt <= 0) return 0;
    u.hp += amt;
    this.emit({ t: 'Healed', target: u.id, source: src, amount: amt, hpAfter: u.hp });
    return amt;
  }

  /**
   * Applies a status. Poison and Parasite reapplied directly restart at the given duration; spread
   * infections pass the duration they inherit. Corrode adds stacks.
   */
  applyStatus(u: Unit, id: StatusId, value: number, src: SourceRef, opts: { spread?: boolean } = {}): void {
    if (u.dead || u.droneState === 'docked' || value <= 0) return;
    const st = u.statuses;
    let refreshed = false;
    switch (id) {
      case 'poison':
      case 'parasite':
      case 'pinned':
      case 'slowed':
        refreshed = !!st[id];
        st[id] = { remaining: value, src };
        break;
      case 'corrode': {
        const dur = this.c.rules.statuses.corrode.duration;
        refreshed = !!st.corrode;
        st.corrode = { stacks: (st.corrode?.stacks ?? 0) + value, remaining: dur > 0 ? dur : null, src };
        break;
      }
    }
    this.emit({ t: 'StatusApplied', target: u.id, status: id, value, source: src, refreshed, spread: !!opts.spread });
  }

  removeStatus(u: Unit, id: StatusId, reason: string): void {
    if (!u.statuses[id]) return;
    delete u.statuses[id];
    this.emit({ t: 'StatusRemoved', target: u.id, status: id, reason });
  }

  // ------------------------------------------------------------------ deaths and the end of the fight

  /**
   * Removes every unit and object marked dead, in unit ID order, leaving enemy corpses and queueing
   * death hooks. The player's death ends the fight at once; the last enemy's death waits for checkWin.
   */
  flushDeaths(): void {
    const deadObjects = this.s.objects.filter((o) => o.destroyed);
    const deadUnits = this.s.units.filter((u) => u.dead).sort((a, b) => a.id - b.id);
    if (!deadObjects.length && !deadUnits.length) return;
    if (deadObjects.length) {
      this.s.objects = this.s.objects.filter((o) => !o.destroyed);
      for (const o of deadObjects) this.emit({ t: 'ObjectDestroyed', id: o.id, kind: o.kind, pos: { ...o.pos }, killer: o.killedBy ?? null });
    }
    for (const u of deadUnits) {
      this.s.units = this.s.units.filter((x) => x !== u);
      this.emit({ t: 'UnitDied', id: u.id, def: u.def, team: u.team, kind: u.kind, pos: { ...u.pos }, killer: u.killedBy ?? null });
      if (u.id === 0) {
        intercomTrigger(this, 'unitDied', { def: 'player' });
        this.endFight({ result: 'lose', cause: describeSource(u.killedBy), round: this.s.round, killer: u.killedBy });
      }
      if (u.team === 'enemy') this.placeCorpse(u);
      intercomTrigger(this, 'unitDied', { def: u.def });
      for (const hook of this.deathHooks) hook(this, u);
    }
  }

  /**
   * Ends the fight in a win once every wave is in and no enemy is left. Only root() calls it, after the action
   * and the chain's queued effects have finished, so a win never cuts an action short: a move that kills the
   * last enemy on one mine still steps on the next, and the last enemy's Spore Burst, Parasite hatch and other
   * death effects still play (and can add an enemy, which continues the fight). The player's death is the
   * opposite case: it ends the fight at once, in flushDeaths, so a loss beats a win from the same action.
   */
  private checkWin(): void {
    if (this.s.outcome || !this.allEnemiesDown()) return;
    this.endFight({ result: 'win', cause: 'All enemies are dead', round: this.s.round });
  }

  private allEnemiesDown(): boolean {
    const inPlay = Math.min(this.s.settings.maxWaves ?? this.c.waves.length, this.c.waves.length);
    return this.s.wavesSpawned >= inPlay && !this.s.units.some((u) => u.team === 'enemy' && !u.dead);
  }

  endFight(outcome: Outcome): never {
    this.s.outcome = outcome;
    this.s.phase = 'ended';
    this.emit({ t: 'FightEnded', result: outcome.result, cause: outcome.cause, round: outcome.round });
    throw new FightOver(outcome);
  }

  // ------------------------------------------------------------------ objects

  placeObject(kind: ObjectKind, pos: Pos, props: Partial<GameObject>, src: SourceRef | null): GameObject {
    const o: GameObject = { id: this.s.nextObjectId++, kind, pos: { ...pos }, createdRound: this.s.round, ...props };
    this.s.objects.push(o);
    this.emit({ t: 'ObjectPlaced', object: { ...o }, source: src });
    return o;
  }

  removeObject(o: GameObject, reason: 'pickup' | 'consumed' | 'decayed' | 'hatched' | 'detonated' | 'replaced'): void {
    this.s.objects = this.s.objects.filter((x) => x !== o);
    this.emit({ t: 'ObjectRemoved', id: o.id, kind: o.kind, pos: { ...o.pos }, reason });
  }

  placeCorpse(u: Unit): void {
    if (this.b.blocksMove(u.pos)) return;
    const old = this.objectAt(u.pos, 'corpse');
    if (old) this.removeObject(old, 'replaced');
    this.placeObject('corpse', u.pos, { of: u.def, decay: this.c.rules.corpseDecay, team: 'enemy' }, null);
  }

  /** A ground unit entered a mine's tile, or a shot reached it: 3x3 explosive blast, then it is consumed. */
  triggerMine(mine: GameObject, by: Unit | null): void {
    this.triggered = true;
    this.immediate(`o${mine.id}`, 'mine_blast', () => {
      this.emit({ t: 'ObjectTriggered', id: mine.id, kind: 'mine', pos: { ...mine.pos }, by: by ? { kind: 'unit', id: by.id } : null });
      this.removeObject(mine, 'detonated');
      const blast = this.c.abilities['proximity_mine']!.blast!;
      const tiles = this.b.square(mine.pos, blast.radius);
      const src = this.playerEffect('proximity_mine', 'object', mine.id, 'mine');
      this.emit({ t: 'AreaEffect', kind: 'blast', tiles, source: src });
      this.areaDamage(tiles, blast.damage, blast.damageType, src);
      if (this.hasUp('H1')) {
        const st = this.up('H1').status!;
        const psrc = this.playerEffect(UP.H1, 'object', mine.id, 'mine');
        for (const u of this.activeUnits().filter((x) => tiles.some((t) => samePos(t, x.pos))).sort((a, b) => a.id - b.id)) {
          this.applyStatus(u, st.id, st.duration, psrc);
        }
      }
    });
  }

  // ------------------------------------------------------------------ movement and spawning

  /**
   * Moves a unit tile by tile along a path. A ground unit entering a mine sets it off, and keeps going
   * if it survives. Returns false if the unit died on the way.
   */
  moveAlong(u: Unit, path: Pos[], mode: MoveMode): boolean {
    let from = { ...u.pos };
    let seg: Pos[] = [];
    for (const p of path) {
      u.pos = { x: p.x, y: p.y };
      seg.push({ ...u.pos });
      if (!u.flying) {
        const mine = this.mineAt(u.pos);
        if (mine) {
          this.emit({ t: 'UnitMoved', id: u.id, from, to: { ...u.pos }, path: seg, mode });
          from = { ...u.pos };
          seg = [];
          this.triggerMine(mine, u);
          if (u.dead) return false;
        }
      }
    }
    if (seg.length) this.emit({ t: 'UnitMoved', id: u.id, from, to: { ...u.pos }, path: seg, mode });
    return true;
  }

  /** The tile itself if free, else the nearest free tile by range distance (ties: lowest row, then column). */
  findSpawnTile(p: Pos): Pos | null {
    if (this.isFree(p)) return { ...p };
    let best: Pos | null = null;
    let bestD = Infinity;
    for (const t of this.b.allTiles()) {
      if (!this.isFree(t)) continue;
      const d = cheb(t, p);
      if (d < bestD || (d === bestD && best && comparePos(t, best) < 0)) {
        best = t;
        bestD = d;
      }
    }
    return best;
  }

  /**
   * Creates a unit with the next ID. A ground unit that appears on a mine sets it off; callers placing
   * several units at once pass triggerMines: false and call triggerMineUnder once all are placed.
   */
  spawnUnit(defId: string, pos: Pos, cause: string, opts: { arrival?: boolean; triggerMines?: boolean } = {}): Unit {
    const def = this.c.units[defId];
    if (!def) throw new Error(`Unknown unit "${defId}"`);
    const u: Unit = {
      id: this.s.nextUnitId++,
      def: defId,
      team: def.team,
      kind: def.kind,
      pos: { ...pos },
      hp: def.hp,
      maxHp: def.hp,
      armor: def.armor,
      flying: !!def.flying,
      elite: !!def.elite,
      statuses: {},
      cooldowns: {},
      spawnRound: this.s.round,
    };
    if (def.ammo !== undefined) u.ammo = u.maxAmmo = def.ammo;
    if (def.charges !== undefined) {
      u.charges = u.maxCharges = this.droneCharges(def.charges);
      u.droneState = 'deployed';
      u.returningPhases = 0;
    }
    if (def.purge) u.cooldowns['purge'] = 0;
    if (opts.arrival) u.arrivalRound = this.s.round;
    this.s.units.push(u);
    this.emit({ t: 'UnitSpawned', unit: structuredClone(u), cause });
    this.s.peakMinions = Math.max(this.s.peakMinions, this.minionCount());
    if (opts.triggerMines !== false) this.triggerMineUnder(u);
    return u;
  }

  /** A ground unit standing on a mine sets it off (used after spawning). */
  triggerMineUnder(u: Unit): void {
    if (u.dead || u.flying) return;
    const mine = this.mineAt(u.pos);
    if (mine) this.triggerMine(mine, u);
  }

  droneCharges(base: number): number {
    return this.hasUp('T6') ? this.up('T6').charges ?? base : base;
  }
}

export function describeSource(src: SourceRef | undefined | null): string {
  if (!src) return 'Unknown';
  const who = src.kind === 'hazard' ? src.def : src.def;
  return `${who} (${src.via})`;
}

export function entityKey(ref: EntityRef): string {
  return ref.kind === 'unit' ? `u${ref.id}` : `o${ref.id}`;
}
