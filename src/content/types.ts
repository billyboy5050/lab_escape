// Content schema. Every number the rules use lives in these structures, loaded from content/*.json.

export type DamageType = 'kinetic' | 'explosive' | 'fire' | 'electric' | 'acid' | 'poison' | 'parasite';
export const DAMAGE_TYPES: readonly DamageType[] = ['kinetic', 'explosive', 'fire', 'electric', 'acid', 'poison', 'parasite'];

export type Team = 'player' | 'enemy';
export type UnitKind = 'player' | 'minion' | 'enemy';
export type StatusId = 'poison' | 'parasite' | 'corrode' | 'pinned' | 'slowed';
export const STATUS_IDS: readonly StatusId[] = ['poison', 'parasite', 'corrode', 'pinned', 'slowed'];

export type DirectionName = 'N' | 'E' | 'S' | 'W';
export const DIRECTION_NAMES: readonly DirectionName[] = ['N', 'E', 'S', 'W'];

export interface RulesDef {
  turn: { ap: number; movement: number };
  sprint: { ap: number; movement: number };
  caps: { minions: number; eggs: number; mines: number; shieldPlacements: number };
  statuses: {
    poison: { damage: number; duration: number; spreadCap: number; damageType: DamageType };
    parasite: { damage: number; duration: number; damageType: DamageType };
    /** 0 means Corrode lasts the rest of the fight. */
    corrode: { duration: number };
    pinned: { duration: number };
    slowed: { duration: number; movementPenalty: number };
  };
  armorAppliesTo: DamageType[];
  corpseDecay: number;
  eggs: {
    hp: number;
    floorTimer: number;
    corpseTimer: number;
    floorHatchlings: number;
    floorVariant: string;
    /** Enemy definition id -> hatchling definition id, or null when that corpse cannot host. */
    corpseHosts: Record<string, string | null>;
  };
  parasiteHatchling: string;
  chainDepthCap: number;
  roundCap: number;
  arrivalRound: 'all' | 'first' | 'none';
  earlyWaveWhenEmpty: boolean;
  loadout: { abilities: number; upgrades: number };
  ui: { showHazardCooldowns: boolean; secondsPerActor: number };
}

export interface AttackDef { range: number; damage: number; damageType: DamageType }
export interface HitDef { damage: number; damageType: DamageType }

export interface UnitDef {
  id: string;
  name: string;
  team: Team;
  kind: UnitKind;
  role?: 'soldier' | 'elite';
  elite?: boolean;
  hatchling?: boolean;
  flying?: boolean;
  hp: number;
  armor: number;
  move: number;
  attack?: AttackDef;
  melee?: HitDef;
  heal?: { amount: number; range: number };
  sweep?: { length: number; damage: number; damageType: DamageType };
  charge?: { minDistance: number; maxMove: number; damage: number; damageType: DamageType };
  purge?: { cooldown: number };
  ammo?: number;
  charges?: number;
  zap?: { range: number; damage: number; chainDamage: number; damageType: DamageType };
  returnMove?: number;
  returnPhases?: number;
  blast?: { radius: number; damage: number; damageType: DamageType };
  ai?: Record<string, number>;
}

export type TargetingKind = 'projectile' | 'grapple' | 'place' | 'adjacent' | 'placePair' | 'lunge' | 'lob' | 'egg';

export interface AbilityDef {
  id: string;
  name: string;
  kit: 'tech' | 'alien';
  ap: number;
  targeting: { kind: TargetingKind; range?: number };
  damage?: number;
  damageType?: DamageType;
  corrode?: number;
  blast?: { radius: number; damage: number; damageType: DamageType };
  area?: { shape: 'square'; radius: number };
  status?: { id: StatusId; duration: number };
  summon?: string;
  segmentHp?: number;
  tags: string[];
  text: string;
}

export interface UtilityDef {
  id: 'sprint' | 'reload' | 'pick_up_mine' | 'redeploy';
  name: string;
  ap: number;
  movement?: number;
  range?: number;
  requiresAbility?: string;
  text: string;
}

export interface UpgradeDef {
  id: string;
  code: string;
  name: string;
  kit: 'tech' | 'alien' | 'hybrid';
  requires: { all?: string[]; any?: string[] };
  hook?: string;
  armorIgnore?: number;
  damage?: number;
  damageType?: DamageType;
  status?: { id: StatusId; duration: number };
  area?: { shape: 'square'; radius: number };
  corrode?: number;
  range?: number;
  charges?: number;
  extraChainHops?: number;
  corpseTimer?: number;
  text: string;
}

export interface HazardDef {
  id: string;
  name: string;
  shape: 'lane' | 'adjacentToTiles';
  radius?: number;
  damage: number;
  damageType: DamageType;
  cooldown: number;
  cancelCooldown: number;
  cue: string;
}

export interface TerrainDef { name: string; blocksMove: boolean; blocksSight: boolean; blocksProjectile: boolean }

export interface MapFile {
  id: string;
  name: string;
  width: number;
  height: number;
  rows: string[];
  legend: Record<string, string>;
  terrain: Record<string, TerrainDef>;
  playerStart: string;
  hazards: { id: string; def: string; tiles: string[]; direction?: DirectionName }[];
}

export interface WaveDef { round: number; units: { def: string; tile: string }[] }

export interface IntercomTrigger {
  type: 'roundStart' | 'hazardPrimed' | 'hazardHitsPlayer' | 'hazardHitsSoldier' | 'unitDied' | 'hpBelow';
  round?: number;
  def?: string;
  threshold?: number;
}
export interface IntercomLineDef { id: string; trigger: IntercomTrigger; text: string; immediate?: boolean }
export interface IntercomDef { lexicon: { code: string; means: string }[]; lines: IntercomLineDef[] }

export interface PresetDef { id: string; name: string; abilities: string[]; upgrades: string[]; expected?: string }

/** The raw JSON files, exactly as they sit in content/. */
export interface ContentFiles {
  rules: RulesDef;
  units: UnitDef[];
  abilities: { abilities: AbilityDef[]; utilities: UtilityDef[] };
  upgrades: UpgradeDef[];
  hazards: HazardDef[];
  map: MapFile;
  waves: WaveDef[];
  intercom: IntercomDef;
  presets: PresetDef[];
}

export interface Content {
  files: ContentFiles;
  rules: RulesDef;
  units: Record<string, UnitDef>;
  abilities: Record<string, AbilityDef>;
  abilityOrder: string[];
  utilities: Record<string, UtilityDef>;
  upgrades: Record<string, UpgradeDef>;
  upgradeOrder: string[];
  hazardDefs: Record<string, HazardDef>;
  map: MapFile;
  waves: WaveDef[];
  intercom: IntercomDef;
  presets: PresetDef[];
  /** Hash of the canonical JSON of every content file. Replays are only valid against the same hash. */
  hash: string;
}
