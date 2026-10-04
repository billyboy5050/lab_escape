import type { GameEvent, GameState, Loadout, SourceRef } from '../state/types';

export type Category = 'player' | 'minion' | 'enemy' | 'hazard';
const zero = (): Record<Category, number> => ({ player: 0, minion: 0, enemy: 0, hazard: 0 });

export interface FightSummary {
  loadout: Loadout;
  outcome: 'win' | 'lose' | 'unfinished';
  cause: string | null;
  rounds: number;
  wavesSpawned: number;
  /** Damage the player took, by who caused it: own abilities, own minions, enemies, hazards. */
  playerDamageTaken: Record<Category, number>;
  /** Damage taken by every player-side unit and object (player, minions, shields, eggs). */
  sideDamageTaken: Record<Category, number>;
  /** Damage dealt to enemies, by source. */
  enemyDamageTaken: Record<Category, number>;
  /** Share of the player's damage taken that came from the player's own side (0 to 1). */
  friendlyFireShare: number;
  friendlyFireToPlayer: number;
  friendlyFireToSide: number;
  causeOfDeath: { category: Category; source: string; via: string } | null;
  abilitiesUsed: Record<string, number>;
  kills: Record<string, number>;
  minionsLost: number;
  peakMinions: number;
  hazardFires: { round: number; hazard: string; victims: string[] }[];
  hazardCancels: number;
  hazardPrimes: number;
  intercomLines: string[];
  /** Round the Warden first damaged the player, or null. */
  wardenReachedPlayerRound: number | null;
  wardenDiedRound: number | null;
  chainSuppressions: number;
}

/** Builds a fight summary from the event stream (fed incrementally, so the simulator need not keep every event). */
export class SummaryBuilder {
  private round = 0;
  private s: FightSummary;
  private unitNames = new Map<number, string>();

  constructor(loadout: Loadout) {
    this.s = {
      loadout,
      outcome: 'unfinished',
      cause: null,
      rounds: 0,
      wavesSpawned: 0,
      playerDamageTaken: zero(),
      sideDamageTaken: zero(),
      enemyDamageTaken: zero(),
      friendlyFireShare: 0,
      friendlyFireToPlayer: 0,
      friendlyFireToSide: 0,
      causeOfDeath: null,
      abilitiesUsed: {},
      kills: {},
      minionsLost: 0,
      peakMinions: 0,
      hazardFires: [],
      hazardCancels: 0,
      hazardPrimes: 0,
      intercomLines: [],
      wardenReachedPlayerRound: null,
      wardenDiedRound: null,
      chainSuppressions: 0,
    };
    this.unitNames.set(0, 'player');
  }

  add(events: readonly GameEvent[]): void {
    const s = this.s;
    for (const e of events) {
      switch (e.t) {
        case 'RoundStarted':
          this.round = e.round;
          s.rounds = e.round;
          break;
        case 'WaveSpawned':
          s.wavesSpawned = Math.max(s.wavesSpawned, e.wave);
          break;
        case 'UnitSpawned':
          this.unitNames.set(e.unit.id, e.unit.def);
          break;
        case 'AbilityUsed':
          s.abilitiesUsed[e.ability] = (s.abilitiesUsed[e.ability] ?? 0) + 1;
          break;
        case 'Sprinted':
          s.abilitiesUsed['sprint'] = (s.abilitiesUsed['sprint'] ?? 0) + 1;
          break;
        case 'Reloaded':
          s.abilitiesUsed['reload'] = (s.abilitiesUsed['reload'] ?? 0) + 1;
          break;
        case 'DroneRedeployed':
          s.abilitiesUsed['redeploy'] = (s.abilitiesUsed['redeploy'] ?? 0) + 1;
          break;
        case 'ObjectRemoved':
          // Picking up a mine is the only action that removes an object for this reason.
          if (e.reason === 'pickup') s.abilitiesUsed['pick_up_mine'] = (s.abilitiesUsed['pick_up_mine'] ?? 0) + 1;
          break;
        case 'DamageDealt': {
          const cat = e.source.category as Category;
          // HP actually lost, so a killing blow's overkill does not inflate the totals.
          const lost = e.hpLost;
          if (e.targetSide === 'player') {
            s.sideDamageTaken[cat] += lost;
            if (e.source.side === 'player') s.friendlyFireToSide += lost;
            if (e.target.kind === 'unit' && e.target.id === 0) {
              s.playerDamageTaken[cat] += lost;
              if (e.source.side === 'player') s.friendlyFireToPlayer += lost;
              if (e.source.def === 'warden' && lost > 0 && s.wardenReachedPlayerRound === null) s.wardenReachedPlayerRound = this.round;
            }
          } else {
            s.enemyDamageTaken[cat] += lost;
          }
          break;
        }
        case 'UnitDied':
          if (e.team === 'enemy') {
            s.kills[e.def] = (s.kills[e.def] ?? 0) + 1;
            if (e.def === 'warden') s.wardenDiedRound = this.round;
          } else if (e.kind === 'minion') s.minionsLost++;
          if (e.id === 0 && e.killer) s.causeOfDeath = { category: e.killer.category as Category, source: sourceName(e.killer), via: e.killer.via };
          break;
        case 'HazardFired':
          s.hazardFires.push({ round: this.round, hazard: e.hazard, victims: e.victims.map((v) => (v.kind === 'unit' ? this.unitNames.get(v.id) ?? `unit ${v.id}` : `object ${v.id}`)) });
          break;
        case 'HazardCancelled':
          s.hazardCancels++;
          break;
        case 'HazardPrimed':
          s.hazardPrimes++;
          break;
        case 'IntercomLine':
          s.intercomLines.push(e.line);
          break;
        case 'ChainSuppressed':
          s.chainSuppressions++;
          break;
        case 'FightEnded':
          s.outcome = e.result;
          s.cause = e.cause;
          s.rounds = e.round;
          break;
      }
    }
  }

  finish(state: GameState): FightSummary {
    const s = this.s;
    s.peakMinions = state.peakMinions;
    s.rounds = state.round;
    if (state.outcome) {
      s.outcome = state.outcome.result;
      s.cause = state.outcome.cause;
    }
    const taken = s.playerDamageTaken.player + s.playerDamageTaken.minion + s.playerDamageTaken.enemy + s.playerDamageTaken.hazard;
    s.friendlyFireShare = taken > 0 ? s.friendlyFireToPlayer / taken : 0;
    return s;
  }
}

export function summarize(events: readonly GameEvent[], state: GameState): FightSummary {
  const b = new SummaryBuilder(state.loadout);
  b.add(events);
  return b.finish(state);
}

export function sourceName(src: SourceRef): string {
  if (src.kind === 'hazard') return String(src.id);
  if (src.kind === 'object') return `${src.def}`;
  return src.def;
}
