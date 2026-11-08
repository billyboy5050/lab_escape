#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace LabEscape.Core
{
    // Content schema. Every number the rules use lives in these structures, loaded from content/*.json.

    public sealed class ApMovement { public int Ap; public int Movement; }
    public sealed class CapsDef { public int Minions; public int Eggs; public int Mines; public int ShieldPlacements; }
    public sealed class PoisonDef { public int Damage; public int Duration; public int SpreadCap; public DamageType DamageType; }
    public sealed class ParasiteDef { public int Damage; public int Duration; public DamageType DamageType; }
    public sealed class SlowedDef { public int Duration; public int MovementPenalty; }

    public sealed class StatusesDef
    {
        public PoisonDef Poison = null!;
        public ParasiteDef Parasite = null!;
        /// <summary>0 means Corrode lasts the rest of the fight.</summary>
        public int CorrodeDuration;
        public int PinnedDuration;
        public SlowedDef Slowed = null!;
    }

    public sealed class EggsDef
    {
        public int Hp;
        public int FloorTimer;
        public int CorpseTimer;
        public int FloorHatchlings;
        public string FloorVariant = "";
        /// <summary>Enemy definition id to hatchling definition id, or null when that corpse cannot host.</summary>
        public OrderedMap<string, string?> CorpseHosts = new OrderedMap<string, string?>();
    }

    public sealed class LoadoutLimits { public int Abilities; public int Upgrades; }
    public sealed class UiDef { public bool ShowHazardCooldowns; public double SecondsPerActor; }

    public sealed class RulesDef
    {
        public ApMovement Turn = null!;
        public ApMovement Sprint = null!;
        public CapsDef Caps = null!;
        public StatusesDef Statuses = null!;
        public List<DamageType> ArmorAppliesTo = new List<DamageType>();
        public int CorpseDecay;
        public EggsDef Eggs = null!;
        public string ParasiteHatchling = "";
        public int ChainDepthCap;
        public int RoundCap;
        public ArrivalRound ArrivalRound;
        public bool EarlyWaveWhenEmpty;
        public LoadoutLimits Loadout = null!;
        public UiDef Ui = null!;
    }

    public sealed class AttackDef { public int Range; public int Damage; public DamageType DamageType; }
    public sealed class HitDef { public int Damage; public DamageType DamageType; }
    public sealed class HealDef { public int Amount; public int Range; }
    public sealed class SweepDef { public int Length; public int Damage; public DamageType DamageType; }
    public sealed class ChargeDef { public int MinDistance; public int MaxMove; public int Damage; public DamageType DamageType; }
    public sealed class PurgeDef { public int Cooldown; }
    public sealed class ZapDef { public int Range; public int Damage; public int ChainDamage; public DamageType DamageType; }
    public sealed class BlastDef { public int Radius; public int Damage; public DamageType DamageType; }

    public sealed class UnitDef
    {
        public string Id = "";
        public string Name = "";
        public Team Team;
        public UnitKind Kind;
        public string? Role;
        public bool Elite;
        public bool Hatchling;
        public bool Flying;
        public int Hp;
        public int Armor;
        public int Move;
        public AttackDef? Attack;
        public HitDef? Melee;
        public HealDef? Heal;
        public SweepDef? Sweep;
        public ChargeDef? Charge;
        public PurgeDef? Purge;
        public int? Ammo;
        public int? Charges;
        public ZapDef? Zap;
        public int? ReturnMove;
        public int? ReturnPhases;
        public BlastDef? Blast;
        public OrderedMap<string, int>? Ai;
    }

    public sealed class TargetingDef { public string Kind = ""; public int? Range; }
    public sealed class StatusDef { public StatusId Id; public int Duration; }
    public sealed class AreaDef { public string Shape = ""; public int Radius; }

    public sealed class AbilityDef
    {
        public string Id = "";
        public string Name = "";
        public string Kit = "";
        public int Ap;
        public TargetingDef Targeting = null!;
        public int? Damage;
        public DamageType? DamageType;
        public int? Corrode;
        public BlastDef? Blast;
        public AreaDef? Area;
        public StatusDef? Status;
        public string? Summon;
        public int? SegmentHp;
        public List<string> Tags = new List<string>();
        public string Text = "";
    }

    public sealed class UtilityDef
    {
        public string Id = "";
        public string Name = "";
        public int Ap;
        public int? Movement;
        public int? Range;
        public string? RequiresAbility;
        public string Text = "";
    }

    public sealed class RequiresDef { public List<string>? All; public List<string>? Any; }

    public sealed class UpgradeDef
    {
        public string Id = "";
        public string Code = "";
        public string Name = "";
        public string Kit = "";
        public RequiresDef Requires = null!;
        public string? Hook;
        public int? ArmorIgnore;
        public int? Damage;
        public DamageType? DamageType;
        public StatusDef? Status;
        public AreaDef? Area;
        public int? Corrode;
        public int? Range;
        public int? Charges;
        public int? ExtraChainHops;
        public int? CorpseTimer;
        public string Text = "";
    }

    public sealed class HazardDef
    {
        public string Id = "";
        public string Name = "";
        public HazardShape Shape;
        public int? Radius;
        public int Damage;
        public DamageType DamageType;
        public int Cooldown;
        public int CancelCooldown;
        public string Cue = "";
    }

    public sealed class TerrainDef
    {
        public string Name = "";
        public bool BlocksMove;
        public bool BlocksSight;
        public bool BlocksProjectile;
    }

    public sealed class MapHazard
    {
        public string Id = "";
        public string Def = "";
        public List<string> Tiles = new List<string>();
        public string? Direction;
    }

    /// <summary>A room ready to fight in: everything that differs from one fight's location to another's.</summary>
    public sealed class RoomDef
    {
        public string Id = "";
        public string Name = "";
        public int Width;
        public int Height;
        public List<string> Rows = new List<string>();
        public OrderedMap<string, string> Legend = new OrderedMap<string, string>();
        public OrderedMap<string, TerrainDef> Terrain = new OrderedMap<string, TerrainDef>();
        public string PlayerStart = "";
        public List<MapHazard> Hazards = new List<MapHazard>();
        public List<WaveDef> Waves = new List<WaveDef>();
        public int RoundCap;
        /// <summary>The room's board, built once with the content and shared by every state that uses it.</summary>
        public Board Board = null!;
    }

    public sealed class WaveUnit { public string Def = ""; public string Tile = ""; }
    public sealed class WaveDef { public int Round; public List<WaveUnit> Units = new List<WaveUnit>(); }

    public static class TriggerTypes
    {
        public const string RoundStart = "roundStart";
        public const string HazardPrimed = "hazardPrimed";
        public const string HazardHitsPlayer = "hazardHitsPlayer";
        public const string HazardHitsSoldier = "hazardHitsSoldier";
        public const string UnitDied = "unitDied";
        public const string HpBelow = "hpBelow";
    }

    public sealed class IntercomTrigger
    {
        public string Type = "";
        public int? Round;
        public string? Def;
        public int? Threshold;
    }

    public sealed class IntercomLineDef
    {
        public string Id = "";
        public IntercomTrigger Trigger = null!;
        public string Text = "";
        public bool Immediate;
    }

    public sealed class LexiconEntry { public string Code = ""; public string Means = ""; }

    public sealed class IntercomDef
    {
        public List<LexiconEntry> Lexicon = new List<LexiconEntry>();
        public List<IntercomLineDef> Lines = new List<IntercomLineDef>();
    }

    public sealed class PresetDef
    {
        public string Id = "";
        public string Name = "";
        public List<string> Abilities = new List<string>();
        public List<string> Upgrades = new List<string>();
        public string? Expected;
    }

    /// <summary>The validated content the engine runs on.</summary>
    public sealed class Content
    {
        public ContentFiles Files = null!;
        public RulesDef Rules = null!;
        public OrderedMap<string, UnitDef> Units = null!;
        public OrderedMap<string, AbilityDef> Abilities = null!;
        public List<string> AbilityOrder = null!;
        public OrderedMap<string, UtilityDef> Utilities = null!;
        public OrderedMap<string, UpgradeDef> Upgrades = null!;
        public List<string> UpgradeOrder = null!;
        public OrderedMap<string, HazardDef> HazardDefs = null!;
        /// <summary>Every room by ID, the Armory first. Fights find theirs with RoomFor.</summary>
        public OrderedMap<string, RoomDef> Rooms = null!;
        /// <summary>The room a fight takes place in when its settings name none: the Armory, built from map.json, waves.json and rules.roundCap.</summary>
        public RoomDef DefaultRoom = null!;
        public IntercomDef Intercom = null!;
        public List<PresetDef> Presets = null!;
        /// <summary>Hash of the canonical JSON of every content file. Replays are only valid against the same hash.</summary>
        public string Hash = "";

        /// <summary>The room a fight takes place in. A fight that names none is in the Armory.</summary>
        public RoomDef RoomFor(string? id)
        {
            if (id == null) return DefaultRoom;
            if (!Rooms.TryGetValue(id, out var room)) throw new ArgumentException($"Unknown room \"{id}\"");
            return room;
        }
    }

    /// <summary>
    /// The raw JSON files, exactly as they sit in content/. Held as parsed JSON because the hash covers the files themselves
    /// and because validation has to look at values that do not fit the typed schema.
    /// </summary>
    public sealed class ContentFiles
    {
        public static readonly IReadOnlyList<string> FileNames = new[] { "rules", "units", "abilities", "upgrades", "hazards", "map", "waves", "intercom", "presets" };

        /// <summary>One object whose keys are the file names.</summary>
        public JObject Root { get; }

        public ContentFiles(JObject root)
        {
            Root = root;
        }

        public JToken? this[string file] => Root[file];

        public ContentFiles DeepClone() => new ContentFiles((JObject)Root.DeepClone());
    }
}
