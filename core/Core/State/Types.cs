#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace LabEscape.Core
{
    public readonly struct EntityRef : IEquatable<EntityRef>
    {
        public readonly EntityKind Kind;
        public readonly int Id;

        public EntityRef(EntityKind kind, int id)
        {
            Kind = kind;
            Id = id;
        }

        public static EntityRef Unit(int id) => new EntityRef(EntityKind.Unit, id);
        public static EntityRef Object(int id) => new EntityRef(EntityKind.Object, id);

        public bool Equals(EntityRef other) => Kind == other.Kind && Id == other.Id;
        public override bool Equals(object? obj) => obj is EntityRef r && Equals(r);
        public override int GetHashCode() => ((int)Kind * 397) ^ Id;
        public static bool operator ==(EntityRef a, EntityRef b) => a.Equals(b);
        public static bool operator !=(EntityRef a, EntityRef b) => !a.Equals(b);
        public override string ToString() => $"{Names.Of(Kind)} {Id}";

        internal void Write(JsonOut o) => o.BeginObject().Key("kind").Value(Names.Of(Kind)).Key("id").Value(Id).EndObject();
    }

    /// <summary>Who caused an effect, for damage attribution, friendly fire and telemetry. Immutable, so clones can share it.</summary>
    public sealed class SourceRef
    {
        /// <summary>The side the effect belongs to. Poison keeps the side of whoever first applied it, through spread.</summary>
        public readonly SourceSide Side;
        /// <summary>Report bucket: the player's own abilities, the player's minions, enemies, or the lab's hazards.</summary>
        public readonly SourceCategory Category;
        public readonly SourceKind Kind;
        /// <summary>The unit or object ID. Meaningless when TextId is set.</summary>
        public readonly int NumberId;
        /// <summary>The hazard ID, for a source that is a hazard; null otherwise.</summary>
        public readonly string? TextId;
        /// <summary>Unit definition, object kind or hazard id of the source.</summary>
        public readonly string Def;
        /// <summary>Ability, attack or effect id, e.g. sidearm, turret_shot, poison, A1_spore_burst.</summary>
        public readonly string Via;

        public SourceRef(SourceSide side, SourceCategory category, SourceKind kind, int id, string def, string via)
            : this(side, category, kind, id, null, def, via)
        {
        }

        public SourceRef(SourceSide side, SourceCategory category, SourceKind kind, string id, string def, string via)
            : this(side, category, kind, 0, id, def, via)
        {
        }

        private SourceRef(SourceSide side, SourceCategory category, SourceKind kind, int numberId, string? textId, string def, string via)
        {
            Side = side;
            Category = category;
            Kind = kind;
            NumberId = numberId;
            TextId = textId;
            Def = def;
            Via = via;
        }

        public SourceRef WithVia(string via) => new SourceRef(Side, Category, Kind, NumberId, TextId, Def, via);

        internal void Write(JsonOut o)
        {
            o.BeginObject().Key("side").Value(Names.Of(Side)).Key("category").Value(Names.Of(Category)).Key("kind").Value(Names.Of(Kind)).Key("id");
            if (TextId != null) o.Value(TextId);
            else o.Value(NumberId);
            o.Key("def").Value(Def).Key("via").Value(Via).EndObject();
        }
    }

    public sealed class TimedStatus
    {
        public int Remaining;
        public SourceRef Src;

        public TimedStatus(int remaining, SourceRef src)
        {
            Remaining = remaining;
            Src = src;
        }

        public TimedStatus Clone() => new TimedStatus(Remaining, Src);
    }

    public sealed class CorrodeStatus
    {
        public int Stacks;
        /// <summary>Null when Corrode lasts the rest of the fight.</summary>
        public int? Remaining;
        public SourceRef Src;

        public CorrodeStatus(int stacks, int? remaining, SourceRef src)
        {
            Stacks = stacks;
            Remaining = remaining;
            Src = src;
        }

        public CorrodeStatus Clone() => new CorrodeStatus(Stacks, Remaining, Src);
    }

    public sealed class Statuses
    {
        public TimedStatus? Poison;
        public TimedStatus? Parasite;
        public CorrodeStatus? Corrode;
        public TimedStatus? Pinned;
        public TimedStatus? Slowed;

        public bool Has(StatusId id)
        {
            switch (id)
            {
                case StatusId.Poison: return Poison != null;
                case StatusId.Parasite: return Parasite != null;
                case StatusId.Corrode: return Corrode != null;
                case StatusId.Pinned: return Pinned != null;
                case StatusId.Slowed: return Slowed != null;
                default: throw new ArgumentOutOfRangeException(nameof(id));
            }
        }

        public void Remove(StatusId id)
        {
            switch (id)
            {
                case StatusId.Poison: Poison = null; break;
                case StatusId.Parasite: Parasite = null; break;
                case StatusId.Corrode: Corrode = null; break;
                case StatusId.Pinned: Pinned = null; break;
                case StatusId.Slowed: Slowed = null; break;
            }
        }

        /// <summary>Sets one of the four statuses that have only a duration. Corrode has stacks and is set directly.</summary>
        public void SetTimed(StatusId id, TimedStatus value)
        {
            switch (id)
            {
                case StatusId.Poison: Poison = value; break;
                case StatusId.Parasite: Parasite = value; break;
                case StatusId.Pinned: Pinned = value; break;
                case StatusId.Slowed: Slowed = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(id));
            }
        }

        public Statuses Clone() => new Statuses
        {
            Poison = Poison?.Clone(),
            Parasite = Parasite?.Clone(),
            Corrode = Corrode?.Clone(),
            Pinned = Pinned?.Clone(),
            Slowed = Slowed?.Clone(),
        };

        private static void Timed(JsonOut o, string key, TimedStatus? st)
        {
            if (st == null) return;
            o.Key(key).BeginObject().Key("remaining").Value(st.Remaining).Key("src");
            st.Src.Write(o);
            o.EndObject();
        }

        internal void Write(JsonOut o)
        {
            o.BeginObject();
            Timed(o, "poison", Poison);
            Timed(o, "parasite", Parasite);
            if (Corrode != null)
            {
                o.Key("corrode").BeginObject().Key("stacks").Value(Corrode.Stacks).Key("remaining");
                if (Corrode.Remaining.HasValue) o.Value(Corrode.Remaining.Value);
                else o.Null();
                o.Key("src");
                Corrode.Src.Write(o);
                o.EndObject();
            }
            Timed(o, "pinned", Pinned);
            Timed(o, "slowed", Slowed);
            o.EndObject();
        }
    }

    public sealed class Unit
    {
        public int Id;
        public string Def = "";
        public Team Team;
        public UnitKind Kind;
        public Pos Pos;
        public int Hp;
        public int MaxHp;
        public int Armor;
        public bool Flying;
        public bool Elite;
        public Statuses Statuses = new Statuses();
        public int? Ammo;
        public int? MaxAmmo;
        public int? Charges;
        public int? MaxCharges;
        public DroneState? DroneState;
        public int? ReturningPhases;
        public OrderedMap<string, int> Cooldowns = new OrderedMap<string, int>(StringComparer.Ordinal);
        public int SpawnRound;
        /// <summary>Set for wave units that arrive under the arrival-round rule: they move but do not act in this round.</summary>
        public int? ArrivalRound;
        /// <summary>Round in which a Purge made this unit immune to poison spread.</summary>
        public int? PurgeImmuneRound;
        /// <summary>Marked dead the moment HP reaches 0; removed once the effect that killed it finishes.</summary>
        public bool Dead;
        public SourceRef? KilledBy;

        public bool Docked => DroneState == Core.DroneState.Docked;

        public Unit Clone()
        {
            var copy = (Unit)MemberwiseClone();
            copy.Statuses = Statuses.Clone();
            copy.Cooldowns = new OrderedMap<string, int>(StringComparer.Ordinal);
            foreach (var pair in Cooldowns) copy.Cooldowns[pair.Key] = pair.Value;
            return copy;
        }

        internal void Write(JsonOut o)
        {
            o.BeginObject();
            o.Key("id").Value(Id).Key("def").Value(Def).Key("team").Value(Names.Of(Team)).Key("kind").Value(Names.Of(Kind));
            o.Key("pos").Pos(Pos).Key("hp").Value(Hp).Key("maxHp").Value(MaxHp).Key("armor").Value(Armor);
            o.Key("flying").Value(Flying).Key("elite").Value(Elite).Key("statuses");
            Statuses.Write(o);
            o.Key("cooldowns").BeginObject();
            foreach (var pair in Cooldowns) o.Key(pair.Key).Value(pair.Value);
            o.EndObject();
            o.Key("spawnRound").Value(SpawnRound);
            // spawnUnit assigns maxAmmo before ammo and maxCharges before charges; the golden logs record that order.
            o.Opt("maxAmmo", MaxAmmo).Opt("ammo", Ammo).Opt("maxCharges", MaxCharges).Opt("charges", Charges);
            if (DroneState.HasValue) o.Key("droneState").Value(Names.Of(DroneState.Value));
            o.Opt("returningPhases", ReturningPhases).Opt("arrivalRound", ArrivalRound).Opt("purgeImmuneRound", PurgeImmuneRound);
            if (Dead) o.Key("dead").Value(true);
            if (KilledBy != null)
            {
                o.Key("killedBy");
                KilledBy.Write(o);
            }
            o.EndObject();
        }
    }

    /// <summary>A mine, egg, shield segment or corpse.</summary>
    public sealed class BoardObject
    {
        public int Id;
        public ObjectKind Kind;
        public Pos Pos;
        public Team? Team;
        public int? Hp;
        public int? MaxHp;
        /// <summary>Egg: rounds left.</summary>
        public int? Timer;
        /// <summary>Egg: "floor" or the corpse's enemy definition.</summary>
        public string? Host;
        public string? HatchInto;
        /// <summary>Corpse: rounds left.</summary>
        public int? Decay;
        /// <summary>Corpse: the enemy definition it came from.</summary>
        public string? Of;
        /// <summary>Shield: placement id.</summary>
        public int? Placement;
        /// <summary>Shield: the last round a projectile was reflected (Reflective Shield).</summary>
        public int? ReflectedRound;
        public int CreatedRound;
        public bool Destroyed;
        public SourceRef? KilledBy;

        public BoardObject Clone() => (BoardObject)MemberwiseClone();

        internal void Write(JsonOut o)
        {
            o.BeginObject();
            o.Key("id").Value(Id).Key("kind").Value(Names.Of(Kind)).Key("pos").Pos(Pos).Key("createdRound").Value(CreatedRound);
            // The golden logs record each kind's fields in the order the rules supply them.
            if (Kind == ObjectKind.Corpse) o.OptText("of", Of).Opt("decay", Decay);
            if (Team.HasValue) o.Key("team").Value(Names.Of(Team.Value));
            o.Opt("hp", Hp).Opt("maxHp", MaxHp).Opt("timer", Timer).OptText("host", Host).OptText("hatchInto", HatchInto);
            if (Kind != ObjectKind.Corpse) o.OptText("of", Of).Opt("decay", Decay);
            o.Opt("placement", Placement).Opt("reflectedRound", ReflectedRound);
            if (Destroyed) o.Key("destroyed").Value(true);
            if (KilledBy != null)
            {
                o.Key("killedBy");
                KilledBy.Write(o);
            }
            o.EndObject();
        }
    }

    public sealed class HazardDecision
    {
        public int Round;
        public HazardStage Stage;
        public HazardResult Result;
        /// <summary>Value rule step (1 to 4) that produced a priming decision; null for firing decisions.</summary>
        public int? Step;
        public string Reason = "";
        public List<EntityRef> Victims = new List<EntityRef>();

        internal void Write(JsonOut o)
        {
            o.BeginObject().Key("round").Value(Round).Key("stage").Value(Names.Of(Stage)).Key("result").Value(Names.Of(Result)).Key("step");
            if (Step.HasValue) o.Value(Step.Value);
            else o.Null();
            o.Key("reason").Value(Reason).Key("victims").Entities(Victims).EndObject();
        }
    }

    public sealed class Hazard
    {
        public string Id = "";
        public string Def = "";
        public List<Pos> Tiles = new List<Pos>();
        public Pos? Dir;
        public HazardState State;
        public int Cooldown;
        public HazardDecision? Last;
        public List<HazardDecision> History = new List<HazardDecision>();

        // A decision is never changed after it is recorded, so clones can share them.
        public Hazard Clone()
        {
            var copy = (Hazard)MemberwiseClone();
            copy.Tiles = new List<Pos>(Tiles);
            copy.History = new List<HazardDecision>(History);
            return copy;
        }

        internal void Write(JsonOut o)
        {
            o.BeginObject().Key("id").Value(Id).Key("def").Value(Def).Key("tiles").PosList(Tiles);
            if (Dir.HasValue) o.Key("dir").Pos(Dir.Value);
            o.Key("state").Value(Names.Of(State)).Key("cooldown").Value(Cooldown);
            if (Last != null)
            {
                o.Key("last");
                Last.Write(o);
            }
            o.Key("history").BeginArray();
            foreach (var d in History) d.Write(o);
            o.EndArray().EndObject();
        }
    }

    public sealed class Loadout
    {
        public List<string> Abilities;
        public List<string> Upgrades;

        public Loadout(IEnumerable<string> abilities, IEnumerable<string> upgrades)
        {
            Abilities = abilities.ToList();
            Upgrades = upgrades.ToList();
        }

        public Loadout Clone() => new Loadout(Abilities, Upgrades);

        internal void Write(JsonOut o) => o.BeginObject().Key("abilities").Texts(Abilities).Key("upgrades").Texts(Upgrades).EndObject();
    }

    /// <summary>Per-fight settings that change rules and therefore travel with the replay.</summary>
    public sealed class FightSettings
    {
        /// <summary>Number of waves in play. Fewer than all holds the later waves back, and clearing the last one in play wins.</summary>
        public int? MaxWaves;

        /// <summary>The ID of the room the fight takes place in. A fight that names none is in the Armory.</summary>
        public string? Room;

        public FightSettings Clone() => new FightSettings { MaxWaves = MaxWaves, Room = Room };

        internal void Write(JsonOut o) => o.BeginObject().Opt("maxWaves", MaxWaves).OptText("room", Room).EndObject();
    }

    public sealed class IntercomEntry
    {
        public int Round;
        public Phase Phase;
        public string Line = "";
        public string Text = "";
    }

    public sealed class IntercomState
    {
        public List<string> Played = new List<string>();
        public List<string> Queue = new List<string>();
        public bool LineThisPhase;
        public List<IntercomEntry> Log = new List<IntercomEntry>();

        public IntercomState Clone() => new IntercomState
        {
            Played = new List<string>(Played),
            Queue = new List<string>(Queue),
            LineThisPhase = LineThisPhase,
            Log = new List<IntercomEntry>(Log),
        };

        internal void Write(JsonOut o)
        {
            o.BeginObject().Key("played").Texts(Played).Key("queue").Texts(Queue).Key("lineThisPhase").Value(LineThisPhase).Key("log").BeginArray();
            foreach (var e in Log)
            {
                o.BeginObject().Key("round").Value(e.Round).Key("phase").Value(Names.Of(e.Phase)).Key("line").Value(e.Line).Key("text").Value(e.Text).EndObject();
            }
            o.EndArray().EndObject();
        }
    }

    public sealed class Outcome
    {
        public FightResult Result;
        public string Cause = "";
        public int Round;
        public SourceRef? Killer;

        internal void Write(JsonOut o)
        {
            o.BeginObject().Key("result").Value(Names.Of(Result)).Key("cause").Value(Cause).Key("round").Value(Round);
            if (Killer != null)
            {
                o.Key("killer");
                Killer.Write(o);
            }
            o.EndObject();
        }
    }

    public sealed class GameState
    {
        public int Round;
        public Phase Phase;
        public List<Unit> Units = new List<Unit>();
        public List<BoardObject> Objects = new List<BoardObject>();
        public List<Hazard> Hazards = new List<Hazard>();
        public int NextUnitId;
        public int NextObjectId;
        public int NextPlacementId;
        public int Ap;
        public int Movement;
        /// <summary>True once the player has spent AP (or triggered something) this turn. Free movement before that can be undone.</summary>
        public bool Committed;
        public int WavesSpawned;
        public IntercomState Intercom = new IntercomState();
        public Loadout Loadout = null!;
        public FightSettings Settings = new FightSettings();
        public Outcome? Outcome;
        public int PeakMinions;

        /// <summary>
        /// A deep copy, where the TypeScript uses structuredClone. Every mutable part is copied; SourceRef, HazardDecision,
        /// IntercomEntry and Outcome are never changed once made, so clones share them.
        /// </summary>
        public GameState Clone()
        {
            var copy = (GameState)MemberwiseClone();
            copy.Units = Units.Select(u => u.Clone()).ToList();
            copy.Objects = Objects.Select(o => o.Clone()).ToList();
            copy.Hazards = Hazards.Select(h => h.Clone()).ToList();
            copy.Intercom = Intercom.Clone();
            copy.Loadout = Loadout.Clone();
            copy.Settings = Settings.Clone();
            return copy;
        }

        /// <summary>The state as one line of JSON, with the fields JSON.stringify would write.</summary>
        public string ToJson()
        {
            var o = new JsonOut();
            o.BeginObject().Key("round").Value(Round).Key("phase").Value(Names.Of(Phase));
            o.Key("units").BeginArray();
            foreach (var u in Units) u.Write(o);
            o.EndArray().Key("objects").BeginArray();
            foreach (var b in Objects) b.Write(o);
            o.EndArray().Key("hazards").BeginArray();
            foreach (var h in Hazards) h.Write(o);
            o.EndArray();
            o.Key("nextUnitId").Value(NextUnitId).Key("nextObjectId").Value(NextObjectId).Key("nextPlacementId").Value(NextPlacementId);
            o.Key("ap").Value(Ap).Key("movement").Value(Movement).Key("committed").Value(Committed).Key("wavesSpawned").Value(WavesSpawned);
            o.Key("intercom");
            Intercom.Write(o);
            o.Key("loadout");
            Loadout.Write(o);
            o.Key("settings");
            Settings.Write(o);
            o.Key("outcome");
            if (Outcome != null) Outcome.Write(o);
            else o.Null();
            o.Key("peakMinions").Value(PeakMinions).EndObject();
            return o.ToString();
        }
    }

    /// <summary>One scored option, recorded for the debug overlay (used for the Flamer's sweep options).</summary>
    public sealed class AiOption
    {
        public Pos Tile;
        public Pos Dir;
        public int Score;
        public int Steps;
        public List<int> Hits = new List<int>();

        internal void Write(JsonOut o)
        {
            o.BeginObject().Key("tile").Pos(Tile).Key("dir").Pos(Dir).Key("score").Value(Score).Key("steps").Value(Steps).Key("hits").Ints(Hits).EndObject();
        }
    }

    /// <summary>A root action and the state right after it. The client animates frame by frame.</summary>
    public sealed class Frame
    {
        public string Label = "";
        public Phase Phase;
        public int? Actor;
        public List<GameEvent> Events = new List<GameEvent>();
        public GameState State = null!;
    }

    public static class GameStates
    {
        /// <summary>Engine version stored in every replay. Bump it when a rule change makes old command logs invalid.</summary>
        public const string EngineVersion = "0.1.1";

        /// <summary>The state before round 1 starts: the player on its start tile, hazards ready, nothing else.</summary>
        public static GameState CreateInitial(Content c, Loadout loadout, FightSettings? settings = null)
        {
            var pdef = c.Units["player"];
            var room = c.RoomFor(settings?.Room);
            var player = new Unit
            {
                Id = 0,
                Def = "player",
                Team = Team.Player,
                Kind = UnitKind.Player,
                Pos = Tiles.Parse(room.PlayerStart),
                Hp = pdef.Hp,
                MaxHp = pdef.Hp,
                Armor = pdef.Armor,
                SpawnRound = 0,
            };
            return new GameState
            {
                Round = 0,
                Phase = Phase.RoundStart,
                Units = new List<Unit> { player },
                Hazards = room.Hazards.Select(h => new Hazard
                {
                    Id = h.Id,
                    Def = h.Def,
                    Tiles = h.Tiles.Select(Tiles.Parse).ToList(),
                    Dir = string.IsNullOrEmpty(h.Direction) ? (Pos?)null : Grid.DirByName(h.Direction!),
                    State = HazardState.Ready,
                    Cooldown = 0,
                }).ToList(),
                NextUnitId = 1,
                NextObjectId = 1,
                NextPlacementId = 1,
                Loadout = loadout.Clone(),
                Settings = settings?.Clone() ?? new FightSettings(),
            };
        }

        public static Unit? PlayerOf(GameState s) => s.Units.FirstOrDefault(u => u.Id == 0);
    }

    internal static class JsonOutExtensions
    {
        public static JsonOut Pos(this JsonOut o, Pos p) => o.BeginObject().Key("x").Value(p.X).Key("y").Value(p.Y).EndObject();

        public static JsonOut PosList(this JsonOut o, IEnumerable<Pos> items)
        {
            o.BeginArray();
            foreach (var p in items) o.Pos(p);
            return o.EndArray();
        }

        public static JsonOut Entities(this JsonOut o, IEnumerable<EntityRef> items)
        {
            o.BeginArray();
            foreach (var e in items) e.Write(o);
            return o.EndArray();
        }

        public static JsonOut Texts(this JsonOut o, IEnumerable<string> items)
        {
            o.BeginArray();
            foreach (var s in items) o.Value(s);
            return o.EndArray();
        }

        public static JsonOut Ints(this JsonOut o, IEnumerable<int> items)
        {
            o.BeginArray();
            foreach (var i in items) o.Value(i);
            return o.EndArray();
        }

        /// <summary>An optional number: left out when absent, as JSON.stringify leaves out undefined.</summary>
        public static JsonOut Opt(this JsonOut o, string key, int? value)
        {
            if (value.HasValue) o.Key(key).Value(value.Value);
            return o;
        }

        public static JsonOut OptText(this JsonOut o, string key, string? value)
        {
            if (value != null) o.Key(key).Value(value);
            return o;
        }
    }
}
