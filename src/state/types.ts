import type { DamageType, StatusId, Team, UnitKind } from '../content/types';
import type { Pos } from '../util/tiles';

export type { Pos } from '../util/tiles';
export type { DamageType, StatusId, Team, UnitKind } from '../content/types';

export type Phase = 'roundStart' | 'player' | 'minion' | 'enemy' | 'environment' | 'ended';

/** Who caused an effect, for damage attribution, friendly fire and telemetry. */
export interface SourceRef {
  /** The side the effect belongs to. Poison keeps the side of whoever first applied it, through spread. */
  side: 'player' | 'enemy' | 'hazard';
  /** Report bucket: the player's own abilities, the player's minions, enemies, or the lab's hazards. */
  category: 'player' | 'minion' | 'enemy' | 'hazard';
  kind: 'unit' | 'object' | 'hazard';
  id: number | string;
  /** Unit definition, object kind or hazard id of the source. */
  def: string;
  /** Ability, attack or effect id, e.g. sidearm, turret_shot, poison, A1_spore_burst. */
  via: string;
}

export interface EntityRef { kind: 'unit' | 'object'; id: number }

export interface TimedStatus { remaining: number; src: SourceRef }

export interface Statuses {
  poison?: TimedStatus;
  parasite?: TimedStatus;
  /** remaining is null when Corrode lasts the rest of the fight. */
  corrode?: { stacks: number; remaining: number | null; src: SourceRef };
  pinned?: TimedStatus;
  slowed?: TimedStatus;
}

export type DroneState = 'deployed' | 'returning' | 'docked';

export interface Unit {
  id: number;
  def: string;
  team: Team;
  kind: UnitKind;
  pos: Pos;
  hp: number;
  maxHp: number;
  armor: number;
  flying: boolean;
  elite: boolean;
  statuses: Statuses;
  ammo?: number;
  maxAmmo?: number;
  charges?: number;
  maxCharges?: number;
  droneState?: DroneState;
  returningPhases?: number;
  cooldowns: Record<string, number>;
  spawnRound: number;
  /** Set for wave units that arrive under the arrival-round rule: they move but do not act in this round. */
  arrivalRound?: number;
  /** Round in which a Purge made this unit immune to poison spread. */
  purgeImmuneRound?: number;
  /** Marked dead the moment HP reaches 0; removed once the effect that killed it finishes. */
  dead?: boolean;
  killedBy?: SourceRef;
}

export type ObjectKind = 'mine' | 'egg' | 'shield' | 'corpse';

export interface GameObject {
  id: number;
  kind: ObjectKind;
  pos: Pos;
  team?: Team;
  hp?: number;
  maxHp?: number;
  /** Egg: rounds left; egg host is 'floor' or the corpse's enemy definition. */
  timer?: number;
  host?: string;
  hatchInto?: string;
  /** Corpse: rounds left and the enemy definition it came from. */
  decay?: number;
  of?: string;
  /** Shield: placement id, and the last round a projectile was reflected (Reflective Shield). */
  placement?: number;
  reflectedRound?: number;
  createdRound: number;
  destroyed?: boolean;
  killedBy?: SourceRef;
}

export type HazardState = 'ready' | 'primed' | 'cooldown';

export interface HazardDecision {
  round: number;
  stage: 'prime' | 'fire';
  result: 'prime' | 'hold' | 'fire' | 'cancel';
  /** Value rule step (1 to 4) that produced a priming decision; null for firing decisions. */
  step: number | null;
  reason: string;
  victims: EntityRef[];
}

export interface Hazard {
  id: string;
  def: string;
  tiles: Pos[];
  dir?: Pos;
  state: HazardState;
  cooldown: number;
  last?: HazardDecision;
  history: HazardDecision[];
}

export interface Loadout { abilities: string[]; upgrades: string[] }

/** Per-fight settings that change rules and therefore travel with the replay. */
export interface FightSettings {
  /** Number of waves in play. Fewer than all holds the later waves back, and clearing the last one in play wins. */
  maxWaves?: number;
}

export interface IntercomEntry { round: number; phase: Phase; line: string; text: string }

export interface IntercomState {
  played: string[];
  queue: string[];
  lineThisPhase: boolean;
  log: IntercomEntry[];
}

export interface Outcome { result: 'win' | 'lose'; cause: string; round: number; killer?: SourceRef }

export interface GameState {
  round: number;
  phase: Phase;
  units: Unit[];
  objects: GameObject[];
  hazards: Hazard[];
  nextUnitId: number;
  nextObjectId: number;
  nextPlacementId: number;
  ap: number;
  movement: number;
  /** True once the player has spent AP (or triggered something) this turn. Free movement before that can be undone. */
  committed: boolean;
  wavesSpawned: number;
  intercom: IntercomState;
  loadout: Loadout;
  settings: FightSettings;
  outcome: Outcome | null;
  peakMinions: number;
}

// ---------------------------------------------------------------------------------------------
// Commands. Tiles are written as names ("D5") so command logs are readable and hand-editable.

export type Command =
  | { type: 'move'; path: string[] }
  | { type: 'sprint' }
  | { type: 'ability'; ability: string; target: string; target2?: string | null; mode?: 'self' | 'unit' }
  | { type: 'reload'; target: string }
  | { type: 'pickUpMine'; target: string }
  | { type: 'redeploy'; drone: number; target: string }
  | { type: 'endTurn' }
  | DebugCommand;

export type DebugCommand =
  | { type: 'debug'; op: 'setHp'; unit: number; value: number }
  | { type: 'debug'; op: 'setAp'; ap: number; movement?: number }
  | { type: 'debug'; op: 'spawn'; def: string; target: string }
  | { type: 'debug'; op: 'forceWave' };

// ---------------------------------------------------------------------------------------------
// Events. The client animates them, telemetry and the simulator consume them.

export type MoveMode = 'walk' | 'fly' | 'pull' | 'grapple' | 'lunge' | 'charge';

export type GameEvent =
  | { t: 'RoundStarted'; round: number }
  | { t: 'PhaseStarted'; round: number; phase: Phase }
  | { t: 'WaveSpawned'; wave: number; units: number[] }
  | { t: 'UnitSpawned'; unit: Unit; cause: string }
  | { t: 'UnitMoved'; id: number; from: Pos; to: Pos; path: Pos[]; mode: MoveMode }
  | { t: 'AbilityUsed'; ability: string; actor: number; target?: Pos; target2?: Pos; mode?: string; ap: number }
  | { t: 'ProjectileFired'; source: SourceRef; from: Pos; path: Pos[]; hit: EntityRef | null; hitPos: Pos | null; reflected?: boolean }
  | {
      t: 'DamageDealt';
      target: EntityRef;
      targetDef: string;
      targetSide: Team;
      pos: Pos;
      source: SourceRef;
      damageType: DamageType;
      raw: number;
      absorbed: number;
      /** Damage after armor: the size of the hit. */
      amount: number;
      /** HP actually removed (the hit without overkill). Summaries count this. */
      hpLost: number;
      hpAfter: number;
      killed: boolean;
    }
  | { t: 'Healed'; target: number; source: SourceRef; amount: number; hpAfter: number }
  | { t: 'UnitDied'; id: number; def: string; team: Team; kind: UnitKind; pos: Pos; killer: SourceRef | null }
  | { t: 'ObjectPlaced'; object: GameObject; source: SourceRef | null }
  | { t: 'ObjectDestroyed'; id: number; kind: ObjectKind; pos: Pos; killer: SourceRef | null }
  | { t: 'ObjectRemoved'; id: number; kind: ObjectKind; pos: Pos; reason: 'pickup' | 'consumed' | 'decayed' | 'hatched' | 'detonated' | 'replaced' }
  | { t: 'ObjectTriggered'; id: number; kind: ObjectKind; pos: Pos; by: EntityRef | null }
  | { t: 'AreaEffect'; kind: 'blast' | 'spore' | 'gas' | 'panel' | 'sweep' | 'burst'; tiles: Pos[]; source: SourceRef }
  | { t: 'ChainArc'; from: Pos; to: Pos; source: SourceRef }
  | { t: 'StatusApplied'; target: number; status: StatusId; value: number; source: SourceRef; refreshed: boolean; spread: boolean }
  | { t: 'StatusTick'; target: number; status: StatusId; remaining: number }
  | { t: 'StatusExpired'; target: number; status: StatusId }
  | { t: 'StatusRemoved'; target: number; status: StatusId; reason: string }
  | { t: 'EggHatched'; id: number; pos: Pos; host: string; spawned: number[]; capped: number }
  | { t: 'DroneDocked'; id: number; pos: Pos }
  | { t: 'DroneRedeployed'; id: number; pos: Pos }
  | { t: 'Reloaded'; id: number; ammo: number }
  | { t: 'Sprinted'; movement: number }
  | { t: 'Purged'; id: number }
  | { t: 'Charged'; id: number; from: Pos; to: Pos; dir: Pos; hit: EntityRef | null }
  | { t: 'HazardPrimed'; hazard: string; step: number; reason: string; victims: EntityRef[] }
  | { t: 'HazardHeld'; hazard: string; step: number; reason: string; victims: EntityRef[] }
  | { t: 'HazardFired'; hazard: string; victims: EntityRef[]; tiles: Pos[] }
  | { t: 'HazardCancelled'; hazard: string; reason: string; victims: EntityRef[] }
  | { t: 'IntercomLine'; line: string; text: string }
  | { t: 'ChainSuppressed'; effect: string; source: string; reason: 'repeat' | 'depth' }
  | { t: 'AIDecision'; id: number; def: string; summary: string; options?: AIOption[] }
  | { t: 'FightEnded'; result: 'win' | 'lose'; cause: string; round: number }
  | { t: 'Debug'; op: string; detail: string };

/** One scored option, recorded for the debug overlay (used for the Flamer's sweep options). */
export interface AIOption { tile: Pos; dir: Pos; score: number; steps: number; hits: number[] }

/** A root action and the state right after it. The client animates frame by frame. */
export interface Frame {
  label: string;
  phase: Phase;
  actor: number | null;
  events: GameEvent[];
  state: GameState;
}
