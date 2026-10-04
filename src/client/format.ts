import type { Content } from '../content/types';
import type { GameEvent, GameObject, GameState, SourceRef, StatusId, Unit } from '../state/types';
import { tileName } from '../util/tiles';

export type LogKind = 'round' | 'phase' | 'ev' | 'ff' | 'hazard' | 'intercom' | 'debug' | 'end';
export interface LogLine {
  id: number;
  text: string;
  kind: LogKind;
}

export function unitName(c: Content, def: string, id?: number): string {
  if (def === 'player') return 'You';
  const n = c.units[def]?.name ?? def;
  return id === undefined ? n : `${n} ${id}`;
}

export function hazardName(c: Content, s: GameState | null, id: string): string {
  const hz = s?.hazards.find((h) => h.id === id) ?? null;
  const def = c.hazardDefs[hz?.def ?? id]?.name ?? id;
  if (hz && hz.def === 'gas_vent') return `${def} ${hz.tiles.map(tileName).join('/')}`;
  return def;
}

const STATUS_WORDS: Record<StatusId, string> = {
  poison: 'poisoned',
  parasite: 'infected with a Parasite',
  corrode: 'corroded',
  pinned: 'Pinned',
  slowed: 'Slowed',
};

/** Turns engine events into combat log lines. Keeps a name registry so dead units stay nameable. */
export class EventFormatter {
  private names = new Map<number, string>();
  private nextId = 1;

  constructor(
    private readonly c: Content,
    initial?: GameState,
  ) {
    if (initial) for (const u of initial.units) this.names.set(u.id, unitName(c, u.def, u.id));
    this.names.set(0, 'You');
  }

  private who(id: number): string {
    return this.names.get(id) ?? `Unit ${id}`;
  }

  private src(s: SourceRef): string {
    if (s.kind === 'hazard') return this.c.hazardDefs[s.def]?.name ?? String(s.id);
    if (s.kind === 'object') return s.def === 'mine' ? 'A mine' : s.def;
    if (s.via === 'A1_spore_burst') return `Spores from ${this.who(s.id as number)}`;
    if (s.via === 'A2_acid_brood') return `Acid from ${this.who(s.id as number)}`;
    if (s.via === 'poison') return `Poison (${s.def === 'player' ? 'yours' : this.who(s.id as number)})`;
    if (s.via === 'parasite' || s.via === 'A3_parasite_jump') return 'Parasite';
    if (s.def === 'player') return 'You';
    return this.who(s.id as number);
  }

  format(e: GameEvent, s: GameState | null, debug = false): LogLine | null {
    const t = this.text(e, s, debug);
    if (!t) return null;
    return { id: this.nextId++, ...t };
  }

  private text(e: GameEvent, s: GameState | null, debug: boolean): { text: string; kind: LogKind } | null {
    const c = this.c;
    switch (e.t) {
      case 'RoundStarted':
        return { text: `Round ${e.round}`, kind: 'round' };
      case 'PhaseStarted':
        if (e.phase === 'roundStart') return null;
        return { text: e.phase === 'player' ? 'Your turn' : `${e.phase[0]!.toUpperCase()}${e.phase.slice(1)} phase`, kind: 'phase' };
      case 'UnitSpawned': {
        const name = unitName(c, e.unit.def, e.unit.id);
        this.names.set(e.unit.id, name);
        const verb = e.cause.startsWith('wave') ? 'arrives at' : e.cause === 'egg' ? 'hatches at' : e.cause === 'parasite' ? 'bursts out at' : 'appears at';
        return { text: `${name} ${verb} ${tileName(e.unit.pos)}`, kind: 'ev' };
      }
      case 'UnitMoved':
        return { text: `${this.who(e.id)} ${e.mode === 'pull' ? 'is pulled' : e.mode === 'charge' ? 'charges' : e.mode === 'lunge' ? 'lunges' : 'moves'} ${tileName(e.from)} → ${tileName(e.to)}`, kind: 'ev' };
      case 'AbilityUsed': {
        const name = c.abilities[e.ability]?.name ?? e.ability;
        const mode = e.mode ? ` (pull ${e.mode})` : '';
        return { text: `You use ${name}${mode}${e.target ? ` on ${tileName(e.target)}` : ''}${e.target2 ? ` and ${tileName(e.target2)}` : ''}`, kind: 'ev' };
      }
      case 'ProjectileFired':
        if (e.reflected) return { text: 'A shield segment reflects the shot', kind: 'ev' };
        if (!e.hit) return e.hitPos ? { text: `${this.src(e.source)}'s shot stops at ${tileName(e.hitPos)}`, kind: 'ev' } : null;
        return null;
      case 'DamageDealt': {
        const target = e.target.kind === 'unit' ? this.who(e.target.id) : `${e.targetDef === 'shield' ? 'Shield segment' : e.targetDef === 'egg' ? 'Egg' : e.targetDef} at ${tileName(e.pos)}`;
        const ff = e.targetSide === 'player' && e.source.side === 'player';
        const armor = e.absorbed ? ` (${e.absorbed} absorbed by armor)` : '';
        const hazard = e.source.kind === 'hazard';
        return {
          text: `${this.src(e.source)} → ${target}: ${e.amount} ${e.damageType}${armor}${e.killed ? ', destroyed' : ''}${ff ? '  [friendly fire]' : ''}`,
          kind: ff ? 'ff' : hazard ? 'hazard' : 'ev',
        };
      }
      case 'Healed':
        return { text: `${this.src(e.source)} heals ${this.who(e.target)} for ${e.amount}`, kind: 'ev' };
      case 'UnitDied':
        return { text: `${this.who(e.id)} dies at ${tileName(e.pos)}`, kind: e.team === 'player' ? 'ff' : 'ev' };
      case 'ObjectPlaced':
        if (e.object.kind === 'corpse') return null;
        return { text: `${e.object.kind === 'shield' ? 'Shield segment' : e.object.kind === 'egg' ? `Egg (${e.object.hatchInto}, ${e.object.timer} round${e.object.timer === 1 ? '' : 's'})` : 'Mine'} placed at ${tileName(e.object.pos)}`, kind: 'ev' };
      case 'ObjectDestroyed':
        return { text: `${e.kind === 'shield' ? 'Shield segment' : e.kind === 'egg' ? 'Egg' : e.kind} at ${tileName(e.pos)} destroyed`, kind: 'ev' };
      case 'ObjectTriggered':
        return { text: `Mine at ${tileName(e.pos)} goes off${e.by ? ` under ${this.who(e.by.id)}` : ''}`, kind: 'ev' };
      case 'ObjectRemoved':
        if (e.reason === 'pickup') return { text: `You pick up the mine at ${tileName(e.pos)}`, kind: 'ev' };
        if (e.reason === 'consumed') return { text: `The corpse at ${tileName(e.pos)} becomes an egg`, kind: 'ev' };
        return null;
      case 'AreaEffect':
        return null;
      case 'StatusApplied': {
        const v = e.status === 'corrode' ? ` (+${e.value})` : e.status === 'poison' || e.status === 'parasite' ? ` (${e.value} rounds)` : '';
        const ff = e.source.side === 'player' && (s?.units.find((u) => u.id === e.target)?.team ?? 'enemy') === 'player';
        if (e.spread) return { text: `Poison spreads to ${this.who(e.target)}${v}${ff ? '  [friendly fire]' : ''}`, kind: ff ? 'ff' : 'ev' };
        return { text: `${this.who(e.target)} is ${STATUS_WORDS[e.status]}${v}${ff && e.status !== 'pinned' ? '  [friendly fire]' : ''}`, kind: ff ? 'ff' : 'ev' };
      }
      case 'StatusExpired':
        return e.status === 'poison' || e.status === 'parasite' ? { text: `${e.status === 'poison' ? 'Poison' : 'Parasite'} wears off ${this.who(e.target)}`, kind: 'ev' } : null;
      case 'StatusRemoved':
        return { text: `${this.who(e.target)} purges its ${e.status}`, kind: 'ev' };
      case 'EggHatched':
        return { text: `Egg at ${tileName(e.pos)} hatches${e.capped ? ` (${e.capped} held back by the minion cap)` : ''}`, kind: 'ev' };
      case 'DroneDocked':
        return { text: `${this.who(e.id)} docks`, kind: 'ev' };
      case 'DroneRedeployed':
        return { text: `${this.who(e.id)} redeploys at ${tileName(e.pos)}`, kind: 'ev' };
      case 'Reloaded':
        return { text: `${this.who(e.id)} reloaded`, kind: 'ev' };
      case 'Sprinted':
        return { text: `You sprint (+${e.movement} movement)`, kind: 'ev' };
      case 'Purged':
        return null;
      case 'Charged':
        return { text: `${this.who(e.id)} charges ${tileName(e.from)} → ${tileName(e.to)}`, kind: 'ev' };
      case 'HazardPrimed': {
        const hz = s?.hazards.find((h) => h.id === e.hazard);
        const cue = hz ? c.hazardDefs[hz.def]?.cue : null;
        return { text: cue ?? `${hazardName(c, s, e.hazard)} stirs`, kind: 'hazard' };
      }
      case 'HazardHeld':
        return debug ? { text: `[debug] ${hazardName(c, s, e.hazard)} holds (step ${e.step}: ${e.reason})`, kind: 'debug' } : null;
      case 'HazardFired':
        return { text: `${hazardName(c, s, e.hazard)} fires`, kind: 'hazard' };
      case 'HazardCancelled':
        return { text: `The cue at the ${hazardName(c, s, e.hazard).toLowerCase()} fades${debug ? ` [${e.reason}]` : ''}`, kind: 'hazard' };
      case 'IntercomLine':
        return { text: `INTERCOM: "${e.text}"`, kind: 'intercom' };
      case 'ChainSuppressed':
        return debug ? { text: `[debug] Chain suppressed: ${e.effect} from ${e.source} (${e.reason})`, kind: 'debug' } : null;
      case 'AIDecision':
        return debug ? { text: `[debug] ${unitName(c, e.def, e.id)}: ${e.summary}`, kind: 'debug' } : null;
      case 'FightEnded':
        return { text: e.result === 'win' ? `Victory: ${e.cause}` : `Defeat: ${e.cause}`, kind: 'end' };
      case 'Debug':
        return { text: `[debug] ${e.detail}`, kind: 'debug' };
      default:
        return null;
    }
  }
}

/** Plain-language description of an object on the board, for the info panel. */
/** The hover summary of a unit: HP, armor, ammo, statuses, and whether it arrived this round (`round` is the current one). */
export function describeUnit(c: Content, u: Unit, round: number): string {
  const parts = [`${u.hp}/${u.maxHp} HP`];
  const armor = Math.max(0, u.armor - (u.statuses.corrode?.stacks ?? 0));
  if (u.armor || u.statuses.corrode) parts.push(`armor ${armor}${u.statuses.corrode ? ` (${u.armor} − ${u.statuses.corrode.stacks} Corrode)` : ''}`);
  if (u.ammo !== undefined) parts.push(`${u.ammo}/${u.maxAmmo} ammo`);
  if (u.charges !== undefined) parts.push(`${u.charges}/${u.maxCharges} charges, ${u.droneState}`);
  if (u.statuses.poison) parts.push(`poisoned ${u.statuses.poison.remaining}`);
  if (u.statuses.parasite) parts.push(`Parasite ${u.statuses.parasite.remaining}`);
  if (u.statuses.pinned) parts.push('Pinned');
  if (u.statuses.slowed) parts.push('Slowed');
  // arrivalRound stays on a wave unit for good; only the round it arrived in is "just arrived".
  if (u.arrivalRound === round) parts.push('just arrived');
  void c;
  return parts.join(' · ');
}

export function describeObject(c: Content, o: GameObject, placing = false): string {
  const eggs = c.rules.eggs;
  switch (o.kind) {
    case 'egg': {
      const n = o.host === 'floor' ? eggs.floorHatchlings : 1;
      const what = `${n === 1 ? 'a' : n} ${(c.units[o.hatchInto ?? '']?.name ?? o.hatchInto ?? 'hatchling').toLowerCase()}${n === 1 ? '' : 's'}`;
      const host = o.host && o.host !== 'floor' ? ` on a ${c.units[o.host]?.name ?? o.host} corpse` : '';
      const when = `${o.timer} round${o.timer === 1 ? '' : 's'}`;
      return placing ? `Places an egg${host}: hatches ${what} in ${when} (${o.hp} HP).` : `Egg${host}: hatches ${what} in ${when}; ${o.hp}/${o.maxHp} HP.`;
    }
    case 'corpse': {
      const variant = eggs.corpseHosts[o.of ?? ''];
      const host = variant ? `A Brood Egg here hatches a ${(c.units[variant]?.name ?? variant).toLowerCase()}.` : 'Cannot host an egg.';
      return `${c.units[o.of ?? '']?.name ?? o.of} corpse: ${o.decay} round${o.decay === 1 ? '' : 's'} left. ${host}`;
    }
    case 'mine': {
      const b = c.abilities['proximity_mine']?.blast;
      return placing ? 'Places a mine.' : `Your mine: any ground unit entering it sets it off (${b?.damage} ${b?.damageType} to its tile and neighbours).`;
    }
    case 'shield':
      return placing ? `Places a shield segment (${o.hp} HP).` : `Shield segment: ${o.hp}/${o.maxHp} HP. Blocks movement and projectiles, not sight.`;
  }
}
